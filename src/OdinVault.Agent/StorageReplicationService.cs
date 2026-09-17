using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;
using OdinVault.Storage.GoogleDrive;

namespace OdinVault.Agent;

public sealed class StorageReplicationService(
    OdinVaultDbContext db,
    ISecretProtector secretProtector,
    GoogleDriveOAuthService googleDriveOAuth,
    IHttpClientFactory httpClientFactory,
    ILogger<StorageReplicationService> logger)
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6)
    ];

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

    public async Task RetryDueAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var due = await db.BackupReplicas
            .Where(x => x.Status == ReplicaStatus.Failed && x.NextRetryAtUtc != null && x.NextRetryAtUtc <= now)
            .OrderBy(x => x.NextRetryAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var replica in due)
        {
            var backup = await db.BackupRecords.FirstOrDefaultAsync(x => x.Id == replica.BackupRecordId, cancellationToken);
            var target = await db.StorageTargets.FirstOrDefaultAsync(x => x.Id == replica.StorageTargetId && x.IsEnabled, cancellationToken);
            if (backup is null || target is null)
                continue;

            await ReplicateToTargetAsync(backup, target, cancellationToken);
        }
    }

    private async Task ReplicateToTargetAsync(BackupRecord backup, StorageTarget target, CancellationToken cancellationToken)
    {
        var replica = await db.BackupReplicas.FirstOrDefaultAsync(
            x => x.BackupRecordId == backup.Id && x.StorageTargetId == target.Id,
            cancellationToken);

        if (replica?.Status == ReplicaStatus.Succeeded)
            return;

        replica ??= new BackupReplica { BackupRecordId = backup.Id, StorageTargetId = target.Id };
        if (db.Entry(replica).State == EntityState.Detached)
            db.BackupReplicas.Add(replica);

        replica.Status = ReplicaStatus.Uploading;
        replica.StartedAtUtc = DateTime.UtcNow;
        replica.CompletedAtUtc = null;
        replica.Error = null;
        replica.NextRetryAtUtc = null;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var provider = CreateProvider(target);
            var result = await provider.UploadAsync(
                new StorageUploadRequest(backup.Id, backup.FileName, backup.FilePath, backup.FileName),
                cancellationToken);

            replica.Status = ReplicaStatus.Succeeded;
            replica.RemoteId = result.RemoteId;
            replica.RemotePath = result.RemotePath;
            replica.SizeBytes = result.SizeBytes;
            replica.CompletedAtUtc = DateTime.UtcNow;
            replica.NextRetryAtUtc = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            replica.Status = ReplicaStatus.Failed;
            replica.Error = ex.Message;
            replica.CompletedAtUtc = DateTime.UtcNow;
            replica.RetryCount++;
            var delay = RetryDelays[Math.Min(replica.RetryCount - 1, RetryDelays.Length - 1)];
            replica.NextRetryAtUtc = DateTime.UtcNow.Add(delay);
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogError(ex, "Backup {BackupId} replication to storage target {StorageTargetId} failed. Retry {RetryCount} at {NextRetryAtUtc}.", backup.Id, target.Id, replica.RetryCount, replica.NextRetryAtUtc);
        }
    }

    private IBackupStorageProvider CreateProvider(StorageTarget target) => target.Type switch
    {
        StorageProviderType.GoogleDrive => CreateGoogleDriveProvider(target),
        StorageProviderType.OdinVaultReplica => CreateReplicaProvider(target),
        _ => throw new NotSupportedException($"Storage provider {target.Type} is not configured for managed replication yet.")
    };

    private IBackupStorageProvider CreateGoogleDriveProvider(StorageTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.ProtectedRefreshToken))
            throw new InvalidOperationException($"Google Drive target '{target.Name}' is not connected.");

        var refreshToken = secretProtector.Unprotect(target.ProtectedRefreshToken);
        return new GoogleDriveBackupStorage(googleDriveOAuth.CreateDriveService(refreshToken), target.FolderId);
    }

    private IBackupStorageProvider CreateReplicaProvider(StorageTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.BaseUrl) || string.IsNullOrWhiteSpace(target.ProtectedApiKey))
            throw new InvalidOperationException($"Replica target '{target.Name}' is not configured.");

        var apiKey = secretProtector.Unprotect(target.ProtectedApiKey);
        return new OdinVaultReplicaStorage(httpClientFactory.CreateClient("OdinVaultReplica"), target.BaseUrl, apiKey);
    }
}
