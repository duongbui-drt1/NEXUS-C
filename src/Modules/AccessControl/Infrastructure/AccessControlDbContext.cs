using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.AccessControl.Domain;
using Nexus.SharedKernel.Context;

namespace Nexus.Modules.AccessControl.Infrastructure;

public sealed class AccessControlDbContext(
    DbContextOptions<AccessControlDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock)
    : ModuleDbContextBase(options, tenantContext, currentUser, clock)
{
    public override string SchemaName => "access_control";

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
            e.HasIndex(x => new { x.NormalizedName, x.TenantId }).IsUnique();
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
            e.HasIndex(x => new { x.UserId, x.RoleId, x.TenantId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.TenantId });
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        base.OnModelCreating(modelBuilder);
    }
}
