using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Integration.Domain;
using Nexus.SharedKernel.Context;

namespace Nexus.Modules.Integration.Infrastructure;

public sealed class IntegrationDbContext(
    DbContextOptions<IntegrationDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock)
    : ModuleDbContextBase(options, tenantContext, currentUser, clock)
{
    public override string SchemaName => "integration";

    public DbSet<ProviderDefinition> Providers => Set<ProviderDefinition>();
    public DbSet<IntegrationConnection> Connections => Set<IntegrationConnection>();
    public DbSet<SyncLog> SyncLogs => Set<SyncLog>();
    public DbSet<IntegrationError> Errors => Set<IntegrationError>();
    public DbSet<WebhookRegistration> Webhooks => Set<WebhookRegistration>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<AiModelConfiguration> AiModels => Set<AiModelConfiguration>();
    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderDefinition>(e =>
        {
            e.ToTable("providers", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Category).HasMaxLength(64).IsRequired();
            e.Property(x => x.Version).HasMaxLength(32).HasDefaultValue("1.0").IsRequired();
            e.Property(x => x.AllowedHosts).HasMaxLength(500);
            e.Property(x => x.ConfigSchemaJson).HasColumnType("jsonb");
            e.Property(x => x.RequiresSecretRef).HasDefaultValue(true).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<IntegrationConnection>(e =>
        {
            e.ToTable("connections", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.ApplicationCode).HasMaxLength(40);
            e.Property(x => x.CredentialReference).HasMaxLength(300).IsRequired();
            e.Property(x => x.NonSecretConfigJson).HasColumnType("jsonb");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.HealthStatus).HasMaxLength(32).HasDefaultValue("Unknown").IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasOne<ProviderDefinition>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SyncLog>(e =>
        {
            e.ToTable("sync_logs", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Direction).HasMaxLength(32).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(1000);
            e.HasOne<IntegrationConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IntegrationError>(e =>
        {
            e.ToTable("integration_errors", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.ErrorCode).HasMaxLength(64).IsRequired();
            e.Property(x => x.Message).HasMaxLength(1500).IsRequired();
            e.HasOne<IntegrationConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WebhookRegistration>(e =>
        {
            e.ToTable("webhooks", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(150).IsRequired();
            e.Property(x => x.TargetUrl).HasMaxLength(500).IsRequired();
            e.Property(x => x.SigningSecretRef).HasMaxLength(300).IsRequired();
            e.Property(x => x.SigningSecretHash).HasMaxLength(128);
            e.Property(x => x.SecretPrefix).HasMaxLength(32);
            e.Property(x => x.SecretVersion).HasDefaultValue(1).IsRequired();
            e.Property(x => x.ApplicationCode).HasMaxLength(40);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.EventType });
        });

        modelBuilder.Entity<WebhookDelivery>(e =>
        {
            e.ToTable("webhook_deliveries", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(150).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.SignatureDigestPrefix).HasMaxLength(32);
            e.Property(x => x.FailureReason).HasMaxLength(1000);
            e.Property(x => x.ResponseSnippet).HasMaxLength(512);
            e.HasIndex(x => new { x.TenantId, x.WebhookId, x.AttemptedAt });
            e.HasOne<WebhookRegistration>().WithMany().HasForeignKey(x => x.WebhookId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AiModelConfiguration>(e =>
        {
            e.ToTable("ai_model_configs", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.ModelCode).HasMaxLength(100).IsRequired();
            e.Property(x => x.ProviderName).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.ModelCode).IsUnique();
        });

        modelBuilder.Entity<AiUsageLog>(e =>
        {
            e.ToTable("ai_usage_logs", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.ModelCode).HasMaxLength(100).IsRequired();
            e.Property(x => x.ProviderName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Status).HasMaxLength(64).IsRequired();
            e.Property(x => x.SanitizedRequestMetaJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TenantId, x.OccurredAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}
