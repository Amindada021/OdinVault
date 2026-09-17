using Microsoft.EntityFrameworkCore;
using OdinVault.Core;

namespace OdinVault.Persistence;

public sealed class OdinVaultDbContext(DbContextOptions<OdinVaultDbContext> options) : DbContext(options)
{
    public DbSet<DatabaseEndpoint> DatabaseEndpoints => Set<DatabaseEndpoint>();
    public DbSet<BackupPolicy> BackupPolicies => Set<BackupPolicy>();
    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();
    public DbSet<StorageTarget> StorageTargets => Set<StorageTarget>();
    public DbSet<DatabaseStorageTarget> DatabaseStorageTargets => Set<DatabaseStorageTarget>();
    public DbSet<BackupReplica> BackupReplicas => Set<BackupReplica>();

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

        modelBuilder.Entity<StorageTarget>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.FolderId).HasMaxLength(500);
            entity.Property(x => x.AccountEmail).HasMaxLength(500);
            entity.Property(x => x.ProtectedRefreshToken).HasMaxLength(8000);
            entity.Property(x => x.BaseUrl).HasMaxLength(2000);
            entity.Property(x => x.ProtectedApiKey).HasMaxLength(8000);
            entity.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<DatabaseStorageTarget>(entity =>
        {
            entity.HasKey(x => new { x.DatabaseEndpointId, x.StorageTargetId });
            entity.HasIndex(x => x.StorageTargetId);
        });

        modelBuilder.Entity<BackupReplica>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RemoteId).HasMaxLength(1000);
            entity.Property(x => x.RemotePath).HasMaxLength(2000);
            entity.Property(x => x.Error).HasMaxLength(4000);
            entity.HasIndex(x => new { x.BackupRecordId, x.StorageTargetId }).IsUnique();
            entity.HasIndex(x => new { x.StorageTargetId, x.StartedAtUtc });
            entity.HasIndex(x => new { x.Status, x.NextRetryAtUtc });
        });
    }
}
