using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Notification.Domain;
using Nexus.SharedKernel.Context;

namespace Nexus.Modules.Notification.Infrastructure;

public sealed class NotificationDbContext(
    DbContextOptions<NotificationDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock)
    : ModuleDbContextBase(options, tenantContext, currentUser, clock)
{
    public override string SchemaName => "notification";

    public DbSet<NotificationTemplate> Templates => Set<NotificationTemplate>();
    public DbSet<NotificationMessage> Messages => Set<NotificationMessage>();
    public DbSet<NotificationPreference> Preferences => Set<NotificationPreference>();
    public DbSet<EmailDeliveryRecord> EmailDeliveries => Set<EmailDeliveryRecord>();
    public DbSet<EmailSuppression> EmailSuppressions => Set<EmailSuppression>();
    public DbSet<EmailWebhookEvent> EmailWebhookEvents => Set<EmailWebhookEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationTemplate>(e =>
        {
            e.ToTable("templates", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.SubjectTemplate).HasMaxLength(300).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        modelBuilder.Entity<NotificationMessage>(e =>
        {
            e.ToTable("notifications", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.RecipientEmail).HasMaxLength(256);
            e.Property(x => x.WebhookUrl).HasMaxLength(500);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(300).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasIndex(x => new { x.TenantId, x.RecipientUserId, x.IsRead });
        });

        modelBuilder.Entity<NotificationPreference>(e =>
        {
            e.ToTable("preferences", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.DigestFrequency).HasMaxLength(32).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        });

        modelBuilder.Entity<EmailDeliveryRecord>(e =>
        {
            e.ToTable("email_deliveries", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.IdempotencyKey).HasMaxLength(150);
            e.Property(x => x.Provider).HasMaxLength(32).IsRequired();
            e.Property(x => x.ProviderMessageId).HasMaxLength(150);
            e.Property(x => x.TemplateKey).HasMaxLength(100);
            e.Property(x => x.RecipientMasked).HasMaxLength(150).IsRequired();
            e.Property(x => x.RecipientHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(300).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.Property(x => x.CorrelationId).HasMaxLength(100);
            e.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
            e.HasIndex(x => x.ProviderMessageId);
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        modelBuilder.Entity<EmailSuppression>(e =>
        {
            e.ToTable("email_suppressions", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.RecipientHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.RecipientMasked).HasMaxLength(150).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(64).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.RecipientHash).IsUnique();
        });

        modelBuilder.Entity<EmailWebhookEvent>(e =>
        {
            e.ToTable("email_webhook_events", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.SvixId).HasMaxLength(150).IsRequired();
            e.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            e.Property(x => x.ProviderMessageId).HasMaxLength(150);
            e.HasIndex(x => x.SvixId).IsUnique();
        });

        base.OnModelCreating(modelBuilder);
    }
}
