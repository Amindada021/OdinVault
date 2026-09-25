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
    public DbSet<BackupJob> BackupJobs => Set<BackupJob>();
    public DbSet<AlertReadState> AlertReadStates => Set<AlertReadState>();

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
            entity.Property(x => x.ContentHashSha256).HasMaxLength(64);
            entity.Property(x => x.Error).HasMaxLength(4000);
            entity.HasIndex(x => new { x.BackupRecordId, x.StorageTargetId }).IsUnique();
            entity.HasIndex(x => new { x.StorageTargetId, x.StartedAtUtc });
            entity.HasIndex(x => new { x.Status, x.NextRetryAtUtc });
        });

        modelBuilder.Entity<AlertReadState>(entity =>
        {
            entity.HasKey(x => x.AlertKey);
            entity.Property(x => x.AlertKey).HasMaxLength(300).IsRequired();
            entity.HasIndex(x => x.ReadAtUtc);
        });

        modelBuilder.Entity<BackupJob>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Stage).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ErrorCode).HasMaxLength(100);
            entity.Property(x => x.ErrorMessage).HasMaxLength(2000);
            entity.HasIndex(x => x.RequestId).IsUnique();
            entity.HasIndex(x => x.DatabaseEndpointId)
                .IsUnique()
                .HasFilter("\"Status\" IN (0, 1)");
            entity.HasIndex(x => new { x.Status, x.CreatedAtUtc, x.Id });
            entity.HasIndex(x => x.BackupRecordId);
            entity.HasOne<DatabaseEndpoint>()
                .WithMany()
                .HasForeignKey(x => x.DatabaseEndpointId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BackupRecord>()
                .WithMany()
                .HasForeignKey(x => x.BackupRecordId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_BackupJobs_Percent",
                "\"Percent\" IS NULL OR (\"Percent\" >= 0 AND \"Percent\" <= 100)"));
        });
    }
}
