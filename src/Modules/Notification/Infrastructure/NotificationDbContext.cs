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
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(300).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasIndex(x => new { x.TenantId, x.RecipientUserId, x.IsRead });
        });

        base.OnModelCreating(modelBuilder);
    }
}
