using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;
using OdinVault.Storage.GoogleDrive;

namespace OdinVault.Agent;

public sealed class StorageReplicationService(
    OdinVaultDbContext db,
    ISecretProtector secretProtector,
    GoogleDriveOAuthService googleDriveOAuth,
    ILogger<StorageReplicationService> logger)
{
    public async Task ReplicateAsync(BackupRecord backup, CancellationToken cancellationToken = default)
    {
        if (backup.Status != BackupStatus.Succeeded || string.IsNullOrWhiteSpace(backup.FilePath) || !File.Exists(backup.FilePath))
            return;

        var targetIds = await db.DatabaseStorageTargets
            .Where(x => x.DatabaseEndpointId == backup.DatabaseEndpointId && x.IsEnabled)
            .Select(x => x.StorageTargetId)
            .ToListAsync(cancellationToken);

        if (targetIds.Count == 0)
            return;

        var targets = await db.StorageTargets
            .Where(x => targetIds.Contains(x.Id) && x.IsEnabled)
            .ToListAsync(cancellationToken);

        foreach (var target in targets)
            await ReplicateToTargetAsync(backup, target, cancellationToken);
    }

    private async Task ReplicateToTargetAsync(
        BackupRecord backup,
        StorageTarget target,
        CancellationToken cancellationToken)
    {
        var replica = await db.BackupReplicas
            .FirstOrDefaultAsync(
                x => x.BackupRecordId == backup.Id && x.StorageTargetId == target.Id,
                cancellationToken);

        if (replica?.Status == ReplicaStatus.Succeeded)
            return;

        replica ??= new BackupReplica
        {
            BackupRecordId = backup.Id,
            StorageTargetId = target.Id
        };

        if (db.Entry(replica).State == EntityState.Detached)
            db.BackupReplicas.Add(replica);

        replica.Status = ReplicaStatus.Uploading;
        replica.StartedAtUtc = DateTime.UtcNow;
        replica.CompletedAtUtc = null;
        replica.Error = null;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var provider = CreateProvider(target);
            var result = await provider.UploadAsync(
                new StorageUploadRequest(
                    backup.Id,
                    backup.FileName,
                    backup.FilePath,
                    backup.FileName),
                cancellationToken);

            replica.Status = ReplicaStatus.Succeeded;
            replica.RemoteId = result.RemoteId;
            replica.RemotePath = result.RemotePath;
            replica.SizeBytes = result.SizeBytes;
            replica.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            replica.Status = ReplicaStatus.Failed;
            replica.Error = ex.Message;
            replica.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogError(
                ex,
                "Backup {BackupId} replication to storage target {StorageTargetId} failed.",
                backup.Id,
                target.Id);
        }
    }

    private IBackupStorageProvider CreateProvider(StorageTarget target) => target.Type switch
    {
        StorageProviderType.GoogleDrive => CreateGoogleDriveProvider(target),
        _ => throw new NotSupportedException($"Storage provider {target.Type} is not configured for managed replication yet.")
    };

    private IBackupStorageProvider CreateGoogleDriveProvider(StorageTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.ProtectedRefreshToken))
            throw new InvalidOperationException($"Google Drive target '{target.Name}' is not connected.");

        var refreshToken = secretProtector.Unprotect(target.ProtectedRefreshToken);
        var drive = googleDriveOAuth.CreateDriveService(refreshToken);
        return new GoogleDriveBackupStorage(drive, target.FolderId);
    }
}
