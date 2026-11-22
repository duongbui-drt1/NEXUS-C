using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Configuration.Domain;
using Nexus.SharedKernel.Context;

namespace Nexus.Modules.Configuration.Infrastructure;

public sealed class ConfigurationDbContext(
    DbContextOptions<ConfigurationDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock)
    : ModuleDbContextBase(options, tenantContext, currentUser, clock)
{
    public override string SchemaName => "configuration";

    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<SettingHistory> SettingHistories => Set<SettingHistory>();
    public DbSet<RateLimitPolicyRecord> RateLimitPolicies => Set<RateLimitPolicyRecord>();
    public DbSet<ReferenceDataItem> ReferenceDataItems => Set<ReferenceDataItem>();
    public DbSet<CurrencyDefinition> Currencies => Set<CurrencyDefinition>();
    public DbSet<ExchangeRateObservation> ExchangeRates => Set<ExchangeRateObservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlatformSetting>(e =>
        {
            e.ToTable("platform_settings", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(120).IsRequired();
            e.Property(x => x.Value).HasMaxLength(4000).IsRequired();
            e.Property(x => x.Category).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
        });

        modelBuilder.Entity<TenantSetting>(e =>
        {
            e.ToTable("tenant_settings", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(120).IsRequired();
            e.Property(x => x.Value).HasMaxLength(4000).IsRequired();
            e.Property(x => x.Category).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
        });

        modelBuilder.Entity<FeatureFlag>(e =>
        {
            e.ToTable("feature_flags", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
        });

        modelBuilder.Entity<SettingHistory>(e =>
        {
            e.ToTable("setting_histories", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Scope).HasMaxLength(32).IsRequired();
            e.Property(x => x.Key).HasMaxLength(120).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Key, x.Version });
        });

        modelBuilder.Entity<RateLimitPolicyRecord>(e =>
        {
            e.ToTable("rate_limit_policies", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.RoutePattern).HasMaxLength(200).IsRequired();
            e.Property(x => x.ScopeType)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(RateLimitScopeType.HumanSession)
                .HasSentinel(RateLimitScopeType.HumanSession)
                .IsRequired();
            e.Property(x => x.Description).HasMaxLength(400);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => new { x.IsEnabled, x.Priority });
        });

        modelBuilder.Entity<ReferenceDataItem>(e =>
        {
            e.ToTable("reference_data", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Category).HasMaxLength(64).IsRequired();
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.Label).HasMaxLength(200).IsRequired();
            e.Property(x => x.LocalizedLabelsJson).HasColumnType("jsonb");
            e.Property(x => x.MetadataJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Category, x.Code }).IsUnique();
            e.HasIndex(x => new { x.Category, x.IsActive, x.SortOrder });
        });

        modelBuilder.Entity<CurrencyDefinition>(e =>
        {
            e.ToTable("currencies", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(3).IsRequired();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Symbol).HasMaxLength(16).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<ExchangeRateObservation>(e =>
        {
            e.ToTable("fx_rates", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.BaseCurrency).HasMaxLength(3).IsRequired();
            e.Property(x => x.QuoteCurrency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Rate).HasPrecision(18, 8).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(64).IsRequired();
            e.Property(x => x.SourceReference).HasMaxLength(300);
            e.HasIndex(x => new { x.BaseCurrency, x.QuoteCurrency, x.FetchedAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}
