using Microsoft.EntityFrameworkCore;

namespace LincleLINK.Core.Infrastructure.Persistence;

/// <summary>
/// EF Core code-first context for the instance metadata database
/// (<c>&lt;DataDirectory&gt;/linclelink.db</c>). Migrations are committed under
/// <c>Migrations/</c> and applied at startup via <c>Database.MigrateAsync</c>
/// (plan 13). The dedup file store in <c>db/</c> stays on disk; only instance
/// manifests live here.
/// </summary>
public sealed class LincleLinkDbContext : DbContext
{
    public LincleLinkDbContext(DbContextOptions<LincleLinkDbContext> options)
        : base(options)
    {
    }

    public DbSet<InstanceEntity> Instances => Set<InstanceEntity>();
    public DbSet<InstanceFileEntity> InstanceFiles => Set<InstanceFileEntity>();
    public DbSet<InstanceDirectoryEntity> InstanceDirectories => Set<InstanceDirectoryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstanceEntity>(b =>
        {
            b.ToTable("Instances");
            b.HasKey(x => x.InstanceName);
            b.Property(x => x.InstanceName).HasMaxLength(255);
            b.Property(x => x.NameKey).IsRequired().HasMaxLength(255);
            b.HasIndex(x => x.NameKey).IsUnique();
            b.Property(x => x.TotalFileSize);
            b.Property(x => x.TotalFileCount);
            b.Property(x => x.TotalFileSizeString).IsRequired().HasMaxLength(32);

            b.HasMany(x => x.Files)
                .WithOne()
                .HasForeignKey(x => x.InstanceName)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Directories)
                .WithOne()
                .HasForeignKey(x => x.InstanceName)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InstanceFileEntity>(b =>
        {
            b.ToTable("InstanceFiles");
            b.HasKey(x => x.Id);
            b.Property(x => x.Ordinal);
            b.Property(x => x.FileName).IsRequired().HasMaxLength(1024);
            b.Property(x => x.RelativePath).IsRequired();
            b.Property(x => x.FileSize);
            b.Property(x => x.HashedFileName).IsRequired().HasMaxLength(64);
            b.HasIndex(x => x.InstanceName);
        });

        modelBuilder.Entity<InstanceDirectoryEntity>(b =>
        {
            b.ToTable("InstanceDirectories");
            b.HasKey(x => x.Id);
            b.Property(x => x.Ordinal);
            b.Property(x => x.Value).IsRequired();
            b.HasIndex(x => x.InstanceName);
        });
    }
}
