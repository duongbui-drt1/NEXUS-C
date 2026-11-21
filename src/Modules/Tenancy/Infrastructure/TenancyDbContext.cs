using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Tenancy.Domain;
using Nexus.SharedKernel.Context;

namespace Nexus.Modules.Tenancy.Infrastructure;

public sealed class TenancyDbContext(
    DbContextOptions<TenancyDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock)
    : ModuleDbContextBase(options, tenantContext, currentUser, clock)
{
    public override string SchemaName => "tenancy";

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<UserTenantMembership> Memberships => Set<UserTenantMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(builder =>
        {
            builder.ToTable("tenants", SchemaName);
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
            builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
            builder.Property(x => x.Slug).HasMaxLength(100).IsRequired();
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
            builder.Property(x => x.DefaultCurrency).HasMaxLength(3).IsRequired();
            builder.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
            builder.Property(x => x.SuspendedReason).HasMaxLength(500);

            builder.HasIndex(x => x.Code).IsUnique();
            builder.HasIndex(x => x.Slug).IsUnique();
            builder.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<UserTenantMembership>(builder =>
        {
            builder.ToTable("memberships", SchemaName);
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.HasIndex(x => new { x.UserId, x.TenantId }).IsUnique();
            builder.HasIndex(x => x.TenantId);
            builder.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        base.OnModelCreating(modelBuilder);
    }
}
