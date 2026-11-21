using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.FileStorage.Domain;
using Nexus.SharedKernel.Context;

namespace Nexus.Modules.FileStorage.Infrastructure;

public sealed class FileStorageDbContext(
    DbContextOptions<FileStorageDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock)
    : ModuleDbContextBase(options, tenantContext, currentUser, clock)
{
    public override string SchemaName => "file_storage";

    public DbSet<StoredFileObject> Files => Set<StoredFileObject>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredFileObject>(e =>
        {
            e.ToTable("stored_files", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
            e.Property(x => x.Sha256Hash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ObjectKey).HasMaxLength(500).IsRequired();
            e.Property(x => x.Category).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.ObjectKey).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}
