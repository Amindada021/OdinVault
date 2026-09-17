using Microsoft.EntityFrameworkCore;
using OdinVault.Core;

namespace OdinVault.Persistence;

public sealed class OdinVaultDbContext(DbContextOptions<OdinVaultDbContext> options) : DbContext(options)
{
    public DbSet<DatabaseEndpoint> DatabaseEndpoints => Set<DatabaseEndpoint>();
    public DbSet<BackupPolicy> BackupPolicies => Set<BackupPolicy>();
    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DatabaseEndpoint>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Host).HasMaxLength(500).IsRequired();
            entity.Property(x => x.DatabaseName).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Username).HasMaxLength(256).IsRequired();
            entity.Property(x => x.ProtectedPassword).HasMaxLength(4000);
            entity.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<BackupPolicy>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BackupDirectory).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.ScheduleCron).HasMaxLength(100);
            entity.HasIndex(x => x.DatabaseEndpointId).IsUnique();
        });

        modelBuilder.Entity<BackupRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileName).HasMaxLength(500).IsRequired();
            entity.Property(x => x.FilePath).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.Error).HasMaxLength(4000);
            entity.HasIndex(x => new { x.DatabaseEndpointId, x.StartedAtUtc });
        });
    }
}
