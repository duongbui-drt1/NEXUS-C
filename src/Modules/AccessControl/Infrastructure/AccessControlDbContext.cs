using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.AccessControl.Domain;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.AccessControl.Infrastructure;

public sealed class AccessControlDbContext(
    DbContextOptions<AccessControlDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock)
    : ModuleDbContextBase(options, tenantContext, currentUser, clock)
{
    private readonly ITenantContext _tenantCtx = tenantContext;
    private readonly ICurrentUser _currentUser = currentUser;

    public override string SchemaName => "access_control";

    public bool IsPlatformScopeOrBypass =>
        IsPlatformBypass || (_currentUser.IsAuthenticated && _currentUser.IsPlatformUser && !CurrentTenantId.HasValue);

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("roles", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Scope).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.ApplicationCode).HasMaxLength(32);
            e.Property(x => x.IsSharedReusable).HasDefaultValue(false).IsRequired();
            e.HasIndex(x => new { x.NormalizedName, x.TenantId }).IsUnique();

            e.HasQueryFilter(r =>
                IsPlatformScopeOrBypass ||
                (r.Scope == RoleScopeType.Tenant && (r.TenantId == null || (CurrentTenantId != null && r.TenantId == CurrentTenantId))));
        });

        modelBuilder.Entity<Permission>(e =>
        {
            e.ToTable("permissions", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(120).IsRequired();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Category).HasMaxLength(64).IsRequired();
            e.Property(x => x.Scope).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions", SchemaName);
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.HasOne(x => x.Role).WithMany(r => r.RolePermissions).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserRole>(e =>
        {
            e.ToTable("user_roles", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.ResourceScopeType).HasMaxLength(64);
            e.Property(x => x.ResourceScopeId).HasMaxLength(128);
            e.Property(x => x.Effect).HasConversion<string>().HasMaxLength(16).HasDefaultValue(AuthorizationEffect.Allow).IsRequired();
            e.HasIndex(x => new { x.UserId, x.RoleId, x.TenantId, x.ResourceScopeType, x.ResourceScopeId, x.Effect }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.TenantId });
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(ur =>
                IsPlatformScopeOrBypass ||
                (CurrentTenantId != null && ur.TenantId == CurrentTenantId));
        });

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceAccessControlWriteGuards();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        EnforceAccessControlWriteGuards();
        return base.SaveChanges();
    }

    private void EnforceAccessControlWriteGuards()
    {
        if (IsPlatformScopeOrBypass)
        {
            return;
        }

        var currentTid = _tenantCtx.TenantId;

        foreach (var entry in ChangeTracker.Entries<Role>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                if (entry.Entity.Scope != RoleScopeType.Tenant ||
                    entry.Entity.IsSystemRole ||
                    !currentTid.HasValue ||
                    entry.Entity.TenantId != currentTid.Value)
                {
                    throw new DomainException(Error.Forbidden(
                        "ACCESS_CONTROL_CROSS_TENANT_WRITE_DENIED",
                        "Tenant context cannot create, modify, or delete platform roles, system roles, or another tenant's roles."));
                }
            }
        }

        foreach (var entry in ChangeTracker.Entries<UserRole>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                if (!currentTid.HasValue || entry.Entity.TenantId != currentTid.Value)
                {
                    throw new DomainException(Error.Forbidden(
                        "ACCESS_CONTROL_CROSS_TENANT_WRITE_DENIED",
                        "Tenant context cannot create, modify, or delete role assignments outside its own tenant."));
                }

                if (entry.State == EntityState.Modified)
                {
                    var originalTenantId = entry.OriginalValues.GetValue<Guid?>(nameof(UserRole.TenantId));
                    if (originalTenantId != currentTid.Value)
                    {
                        throw new DomainException(Error.Forbidden(
                            "ACCESS_CONTROL_CROSS_TENANT_WRITE_DENIED",
                            "Cannot reassign a user role across tenants."));
                    }
                }
            }
        }
    }
}
