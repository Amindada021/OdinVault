using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;

namespace OdinVault.Agent;

public sealed class BackupJobRecoveryService(
    OdinVaultDbContext db,
    ILogger<BackupJobRecoveryService> logger)
{
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var runningJobs = await db.BackupJobs
            .Where(x => x.Status == BackupJobStatus.Running)
            .ToListAsync(cancellationToken);

        var linkedBackupIds = runningJobs
            .Where(x => x.BackupRecordId != null)
            .Select(x => x.BackupRecordId!.Value)
            .Distinct()
            .ToList();

        var successfulLinkedBackups = linkedBackupIds.Count == 0
            ? new Dictionary<Guid, BackupRecord>()
            : await db.BackupRecords
                .AsNoTracking()
                .Where(x => linkedBackupIds.Contains(x.Id) && x.Status == BackupStatus.Succeeded)
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var staleBackupRecords = await db.BackupRecords
            .Where(x => x.Status == BackupStatus.Running)
            .ToListAsync(cancellationToken);

        foreach (var backup in staleBackupRecords)
        {
            backup.Status = BackupStatus.Failed;
            if (backup.VerificationStatus == VerificationStatus.Pending)
                backup.VerificationStatus = VerificationStatus.Failed;
            backup.CompletedAtUtc = now;
            backup.Error = "Agent restarted before the backup operation completed.";
        }

        var staleReplicas = await db.BackupReplicas
            .Where(x => x.Status == ReplicaStatus.Uploading)
            .ToListAsync(cancellationToken);

        foreach (var replica in staleReplicas)
        {
            replica.Status = ReplicaStatus.Failed;
            replica.Error = "Agent restarted before the replication operation completed.";
            replica.CompletedAtUtc = now;
            replica.RetryCount++;
            replica.NextRetryAtUtc = now;
        }

        var recoveredSucceeded = 0;
        var interrupted = 0;
        var recoveredBackupIds = new List<Guid>();

        foreach (var job in runningJobs)
        {
            if (job.BackupRecordId is Guid backupId &&
                successfulLinkedBackups.TryGetValue(backupId, out var backup) &&
                backup.DatabaseEndpointId == job.DatabaseEndpointId)
            {
                job.Status = BackupJobStatus.Succeeded;
                job.Stage = "complete";
                job.Percent = 100;
                job.CompletedAtUtc = now;
                job.UpdatedAtUtc = now;
                job.ErrorCode = null;
                job.ErrorMessage = null;
                recoveredSucceeded++;
                recoveredBackupIds.Add(backup.Id);
                continue;
            }

            job.Status = BackupJobStatus.Interrupted;
            job.Stage = "interrupted";
            job.CompletedAtUtc = now;
            job.UpdatedAtUtc = now;
            job.ErrorCode = "agent_restarted";
            job.ErrorMessage = "اجرای قبلی با راه‌اندازی مجدد Agent متوقف شد؛ در صورت نیاز بکاپ را دوباره اجرا کنید.";
            interrupted++;
        }

        foreach (var backupId in recoveredBackupIds.Distinct())
        {
            var backup = successfulLinkedBackups[backupId];
            var requiredTargetIds = await (
                from link in db.DatabaseStorageTargets
                join target in db.StorageTargets on link.StorageTargetId equals target.Id
                where link.DatabaseEndpointId == backup.DatabaseEndpointId &&
                      link.IsEnabled &&
                      target.IsEnabled
                select target.Id)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (requiredTargetIds.Count == 0)
                continue;

            var existingTargetIds = await db.BackupReplicas
                .Where(x => x.BackupRecordId == backupId &&
                            requiredTargetIds.Contains(x.StorageTargetId))
                .Select(x => x.StorageTargetId)
                .ToListAsync(cancellationToken);

            foreach (var targetId in requiredTargetIds.Except(existingTargetIds))
            {
                db.BackupReplicas.Add(new BackupReplica
                {
                    BackupRecordId = backupId,
                    StorageTargetId = targetId,
                    Status = ReplicaStatus.Failed,
                    Error = "Replication was not confirmed before the Agent restarted.",
                    RetryCount = 0,
                    NextRetryAtUtc = now,
                    StartedAtUtc = now,
                    CompletedAtUtc = now
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (runningJobs.Count > 0 || staleBackupRecords.Count > 0 || staleReplicas.Count > 0)
        {
            logger.LogWarning(
                "Startup recovery reconciled {RunningJobs} running jobs ({SucceededJobs} succeeded, {InterruptedJobs} interrupted), {RunningBackups} running backup records and {UploadingReplicas} uploading replicas.",
                runningJobs.Count,
                recoveredSucceeded,
                interrupted,
                staleBackupRecords.Count,
                staleReplicas.Count);
        }
    }
}
