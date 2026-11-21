using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Audit.Domain;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Audit.Infrastructure;

public sealed class AuditDbContext(
    DbContextOptions<AuditDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock)
    : ModuleDbContextBase(options, tenantContext, currentUser, clock)
{
    public override string SchemaName => "audit";

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.ToTable("audit_events", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.ActorEmail).HasMaxLength(256);
            e.Property(x => x.Action).HasMaxLength(150).IsRequired();
            e.Property(x => x.ResourceType).HasMaxLength(100).IsRequired();
            e.Property(x => x.ResourceId).HasMaxLength(128);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(500);
            e.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Result).HasMaxLength(64).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.BeforeMetadataJson).HasColumnType("jsonb");
            e.Property(x => x.AfterMetadataJson).HasColumnType("jsonb");

            e.HasIndex(x => new { x.TenantId, x.OccurredAt });
            e.HasIndex(x => x.CorrelationId);
            e.HasIndex(x => x.Action);
        });

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceAppendOnly();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        EnforceAppendOnly();
        return base.SaveChanges();
    }

    private void EnforceAppendOnly()
    {
        var mutated = ChangeTracker.Entries<AuditEvent>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);

        if (mutated)
        {
            throw new DomainException(Error.Forbidden(
                "AUDIT_IMMUTABLE",
                "Audit events are strictly append-only and cannot be modified or deleted."));
        }
    }
}
