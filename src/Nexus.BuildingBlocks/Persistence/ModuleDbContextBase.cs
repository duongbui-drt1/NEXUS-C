using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Messaging;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Events;
using Nexus.SharedKernel.Primitives;
using Nexus.SharedKernel.Results;

namespace Nexus.BuildingBlocks.Persistence;

public abstract class ModuleDbContextBase : DbContext
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ISystemClock _clock;

    protected ModuleDbContextBase(
        DbContextOptions options,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ISystemClock clock)
        : base(options)
    {
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
    }

    public abstract string SchemaName { get; }
    protected virtual bool OwnsOutboxTable => false;

    public Guid? CurrentTenantId => _tenantContext.TenantId;
    public bool IsPlatformBypass => _tenantContext.IsPlatformBypass;

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public void StageOutboxEvent(EventEnvelope envelope)
    {
        OutboxMessages.Add(OutboxMessage.FromEnvelope(envelope));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages", "messaging", t =>
            {
                if (!OwnsOutboxTable)
                {
                    t.ExcludeFromMigrations();
                }
            });

            builder.HasKey(x => x.Id);
            builder.Property(x => x.EventId).IsRequired();
            builder.Property(x => x.EventType).HasMaxLength(200).IsRequired();
            builder.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            builder.Property(x => x.Producer).HasMaxLength(100).HasDefaultValue("nexus-c").IsRequired();
            builder.Property(x => x.CausationId).HasMaxLength(100);
            builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.Property(x => x.LastError).HasMaxLength(2000);
            if (OwnsOutboxTable)
            {
                builder.HasIndex(x => x.EventId).IsUnique();
                builder.HasIndex(x => new { x.Status, x.NextAttemptAt });
                builder.HasIndex(x => x.TenantId);
            }
        });

        ConfigureConventionsAndFilters(modelBuilder);
    }

    protected void ConfigureConventionsAndFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (clrType == typeof(OutboxMessage))
            {
                continue;
            }

            if (typeof(AuditableEntity).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType)
                    .Property(nameof(AuditableEntity.RowVersion))
                    .IsRowVersion();
            }

            if (typeof(ITenantScoped).IsAssignableFrom(clrType))
            {
                var parameter = Expression.Parameter(clrType, "e");
                var tenantProp = Expression.Property(parameter, nameof(ITenantScoped.TenantId));
                var ctxTenantProp = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
                var ctxBypassProp = Expression.Property(Expression.Constant(this), nameof(IsPlatformBypass));

                var hasValue = Expression.Property(ctxTenantProp, nameof(Nullable<Guid>.HasValue));
                var value = Expression.Property(ctxTenantProp, nameof(Nullable<Guid>.Value));
                var tenantMatches = Expression.AndAlso(hasValue, Expression.Equal(tenantProp, value));
                var body = Expression.OrElse(ctxBypassProp, tenantMatches);

                if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
                {
                    var isDeletedProp = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
                    body = Expression.AndAlso(body, Expression.Not(isDeletedProp));
                }

                var lambda = Expression.Lambda(body, parameter);
                modelBuilder.Entity(clrType).HasQueryFilter(lambda);
            }
            else if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                var parameter = Expression.Parameter(clrType, "e");
                var isDeletedProp = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
                var lambda = Expression.Lambda(Expression.Not(isDeletedProp), parameter);
                modelBuilder.Entity(clrType).HasQueryFilter(lambda);
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndTenantGuards();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditAndTenantGuards();
        return base.SaveChanges();
    }

    private void ApplyAuditAndTenantGuards()
    {
        var now = _clock.UtcNow;
        var actorId = _currentUser.UserId;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.SetCreatedAudit(actorId, now);
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.SetUpdatedAudit(actorId, now);
            }
        }

        foreach (var entry in ChangeTracker.Entries<TenantScopedEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.TenantId == Guid.Empty)
                {
                    if (_tenantContext.HasTenant)
                    {
                        entry.Entity.AssignTenant(_tenantContext.TenantId!.Value);
                    }
                    else
                    {
                        throw new DomainException(Error.Forbidden(
                            "TENANT_CONTEXT_REQUIRED",
                            "A valid tenant context is required to create tenant-scoped resources."));
                    }
                }
                else if (!_tenantContext.IsPlatformBypass &&
                         (!_tenantContext.HasTenant || entry.Entity.TenantId != _tenantContext.TenantId!.Value))
                {
                    throw new DomainException(Error.Forbidden(
                        "CROSS_TENANT_WRITE_DENIED",
                        "Cannot create a resource belonging to another tenant."));
                }
            }
            else if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (!_tenantContext.IsPlatformBypass &&
                    (!_tenantContext.HasTenant || entry.Entity.TenantId != _tenantContext.TenantId!.Value))
                {
                    throw new DomainException(Error.Forbidden(
                        "CROSS_TENANT_WRITE_DENIED",
                        "Cannot modify or delete a resource belonging to another tenant."));
                }
            }
        }
    }
}
