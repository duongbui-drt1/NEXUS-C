using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Organization.Domain;
using Nexus.SharedKernel.Context;

namespace Nexus.Modules.Organization.Infrastructure;

public sealed class OrganizationDbContext(
    DbContextOptions<OrganizationDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock)
    : ModuleDbContextBase(options, tenantContext, currentUser, clock)
{
    public override string SchemaName => "organization";

    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<OrganizationUnit> OrganizationUnits => Set<OrganizationUnit>();
    public DbSet<UserOrganizationAssignment> UserAssignments => Set<UserOrganizationAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LegalEntity>(builder =>
        {
            builder.ToTable("legal_entities", SchemaName);
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
            builder.Property(x => x.LegalName).HasMaxLength(250).IsRequired();
            builder.Property(x => x.TaxIdentificationNumber).HasMaxLength(64);
            builder.Property(x => x.Address).HasMaxLength(500);
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            builder.HasIndex(x => x.TenantId);
        });

        modelBuilder.Entity<Branch>(builder =>
        {
            builder.ToTable("branches", SchemaName);
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
            builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
            builder.Property(x => x.Address).HasMaxLength(500);
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            builder.HasIndex(x => x.TenantId);
            builder.HasOne<LegalEntity>()
                .WithMany()
                .HasForeignKey(x => x.LegalEntityId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrganizationUnit>(builder =>
        {
            builder.ToTable("organization_units", SchemaName);
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
            builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
            builder.Property(x => x.UnitType).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            builder.HasIndex(x => x.TenantId);
            builder.HasOne<OrganizationUnit>()
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserOrganizationAssignment>(builder =>
        {
            builder.ToTable("user_organization_assignments", SchemaName);
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Title).HasMaxLength(150);
            builder.HasIndex(x => new { x.TenantId, x.UserId });
            builder.HasOne<Branch>()
                .WithMany()
                .HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<OrganizationUnit>()
                .WithMany()
                .HasForeignKey(x => x.OrganizationUnitId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        base.OnModelCreating(modelBuilder);
    }
}
