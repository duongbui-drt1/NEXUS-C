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

        base.OnModelCreating(modelBuilder);
    }
}
