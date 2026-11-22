using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Subscription.Domain;
using Nexus.SharedKernel.Context;

namespace Nexus.Modules.Subscription.Infrastructure;

public sealed class SubscriptionDbContext(
    DbContextOptions<SubscriptionDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock)
    : ModuleDbContextBase(options, tenantContext, currentUser, clock)
{
    public override string SchemaName => "subscription";

    public DbSet<RegisteredApplication> Applications => Set<RegisteredApplication>();
    public DbSet<TenantApplicationActivation> TenantApplications => Set<TenantApplicationActivation>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
    public DbSet<TenantSubscription> Subscriptions => Set<TenantSubscription>();
    public DbSet<SubscriptionItem> SubscriptionItems => Set<SubscriptionItem>();
    public DbSet<Entitlement> Entitlements => Set<Entitlement>();
    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RegisteredApplication>(e =>
        {
            e.ToTable("applications", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(32).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Owner).HasMaxLength(150).HasDefaultValue("NEXUS Platform").IsRequired();
            e.Property(x => x.TrustClassification).HasConversion<string>().HasMaxLength(32).HasDefaultValue(ApplicationTrustClassification.FirstParty).HasSentinel(ApplicationTrustClassification.FirstParty).IsRequired();
            e.Property(x => x.AllowedAuthMethods).HasMaxLength(250).HasDefaultValue("jwt,oauth2_client_credentials").IsRequired();
            e.Property(x => x.RedirectOrigins).HasMaxLength(1000);
            e.Property(x => x.CurrentVersion).HasMaxLength(32).IsRequired();
            e.Property(x => x.ReleaseChannel).HasMaxLength(32).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.MinPlatformVersion).HasMaxLength(32).IsRequired();
            e.Property(x => x.RequiredCapabilities).HasMaxLength(1000);
            e.Property(x => x.ModuleDependencies).HasMaxLength(500);
            e.Property(x => x.CompatibilityMetadataJson).HasColumnType("jsonb");
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<TenantApplicationActivation>(e =>
        {
            e.ToTable("tenant_applications", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.ApplicationCode).HasMaxLength(32).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.ApplicationCode }).IsUnique();
        });

        modelBuilder.Entity<Plan>(e =>
        {
            e.ToTable("plans", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.MonthlyPrice).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Version).HasDefaultValue(1).IsRequired();
            e.Property(x => x.ArchiveReason).HasMaxLength(500);
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<PlanFeature>(e =>
        {
            e.ToTable("plan_features", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.FeatureCode).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.PlanId, x.FeatureCode }).IsUnique();
            e.HasOne<Plan>().WithMany(p => p.Features).HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TenantSubscription>(e =>
        {
            e.ToTable("subscriptions", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SubscriptionItem>(e =>
        {
            e.ToTable("subscription_items", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.ProductCode).HasMaxLength(64).IsRequired();
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.HasOne<TenantSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Entitlement>(e =>
        {
            e.ToTable("entitlements", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.FeatureCode).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.FeatureCode }).IsUnique();
            e.HasOne<TenantSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UsageRecord>(e =>
        {
            e.ToTable("usage_records", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.FeatureCode).HasMaxLength(64).IsRequired();
            e.Property(x => x.IdempotencyKey).HasMaxLength(150);
            e.HasIndex(x => new { x.TenantId, x.FeatureCode, x.RecordedAt });
            e.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
        });

        base.OnModelCreating(modelBuilder);
    }
}
