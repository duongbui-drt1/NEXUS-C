using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Messaging;
using Nexus.Modules.Identity.Domain;
using Nexus.SharedKernel.Events;

namespace Nexus.Modules.Identity.Infrastructure;

public sealed class IdentityModuleDbContext(DbContextOptions<IdentityModuleDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public const string SchemaName = "identity";

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public void StageOutboxEvent(EventEnvelope envelope)
    {
        OutboxMessages.Add(OutboxMessage.FromEnvelope(envelope));
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(SchemaName);

        builder.Entity<ApplicationUser>().ToTable("users", SchemaName);
        builder.Entity<IdentityRole<Guid>>().ToTable("asp_net_roles", SchemaName);
        builder.Entity<IdentityUserRole<Guid>>().ToTable("asp_net_user_roles", SchemaName);
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("asp_net_user_claims", SchemaName);
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("asp_net_user_logins", SchemaName);
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("asp_net_role_claims", SchemaName);
        builder.Entity<IdentityUserToken<Guid>>().ToTable("asp_net_user_tokens", SchemaName);

        builder.Entity<UserProfile>(e =>
        {
            e.ToTable("user_profiles", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(150);
            e.Property(x => x.PreferredLocale).HasMaxLength(20).IsRequired();
            e.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserProfile>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<UserSession>(e =>
        {
            e.ToTable("sessions", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(500);
            e.Property(x => x.RevokedReason).HasMaxLength(200);
            e.HasIndex(x => new { x.UserId, x.RevokedAt });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.RevokedReason).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.TokenFamilyId);
            e.HasIndex(x => x.SessionId);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ExternalIdentity>(e =>
        {
            e.ToTable("external_identities", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(64).IsRequired();
            e.Property(x => x.ProviderSubject).HasMaxLength(256).IsRequired();
            e.Property(x => x.ProviderEmail).HasMaxLength(256);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.Provider, x.ProviderSubject }).IsUnique();
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LoginAttempt>(e =>
        {
            e.ToTable("login_attempts", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.FailureReason).HasMaxLength(200);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(500);
            e.HasIndex(x => new { x.Email, x.AttemptedAt });
        });

        builder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_messages", "messaging", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        });
    }
}
