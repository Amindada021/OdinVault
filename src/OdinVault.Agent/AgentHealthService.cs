using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;

namespace OdinVault.Agent;

public sealed record AgentPaths(
    string DataDirectory,
    string StorageDirectory,
    string ReplicaDirectory);

public sealed record AgentHealthSnapshot(
    string Status,
    string ProtectionStatus,
    DateTime Utc,
    bool SqliteAvailable,
    int EnabledDatabases,
    int QueuedJobs,
    int RunningJobs,
    int StaleRunningJobs,
    int FailedJobsLast24Hours,
    int FailedReplicasDue,
    int DatabasesWithoutSuccessfulBackup,
    long? StorageFreeBytes,
    long? ReplicaFreeBytes);

public sealed class AgentHealthService(
    OdinVaultDbContext db,
    AgentPaths paths,
    ILogger<AgentHealthService> logger)
{
    public async Task<AgentHealthSnapshot> CheckAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
                return Unavailable(now);

            var staleCutoff = now.AddHours(-6);
            var failedCutoff = now.AddHours(-24);

            var enabledDatabases = await db.DatabaseEndpoints
                .AsNoTracking()
                .CountAsync(x => x.IsEnabled, cancellationToken);

            var queuedJobs = await db.BackupJobs
                .AsNoTracking()
                .CountAsync(x => x.Status == BackupJobStatus.Queued, cancellationToken);

            var runningJobs = await db.BackupJobs
                .AsNoTracking()
                .CountAsync(x => x.Status == BackupJobStatus.Running, cancellationToken);

            var staleRunningJobs = await db.BackupJobs
                .AsNoTracking()
                .CountAsync(x => x.Status == BackupJobStatus.Running &&
                                 x.UpdatedAtUtc < staleCutoff, cancellationToken);

            var failedJobsLast24Hours = await db.BackupJobs
                .AsNoTracking()
                .CountAsync(x => (x.Status == BackupJobStatus.Failed ||
                                  x.Status == BackupJobStatus.Interrupted) &&
                                 x.CompletedAtUtc != null &&
                                 x.CompletedAtUtc >= failedCutoff, cancellationToken);

            var failedReplicasDue = await db.BackupReplicas
                .AsNoTracking()
                .CountAsync(x => x.Status == ReplicaStatus.Failed &&
                                 x.NextRetryAtUtc != null &&
                                 x.NextRetryAtUtc <= now, cancellationToken);

            var enabledDatabaseIds = await db.DatabaseEndpoints
                .AsNoTracking()
                .Where(x => x.IsEnabled)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            var locallyProtectedDatabaseIds = await db.BackupRecords
                .AsNoTracking()
                .Where(x => enabledDatabaseIds.Contains(x.DatabaseEndpointId) &&
                            x.Status == BackupStatus.Succeeded &&
                            x.LocalFileAvailable)
                .Select(x => x.DatabaseEndpointId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var replicaProtectedDatabaseIds = await (
                from backup in db.BackupRecords.AsNoTracking()
                join replica in db.BackupReplicas.AsNoTracking()
                    on backup.Id equals replica.BackupRecordId
                join target in db.StorageTargets.AsNoTracking()
                    on replica.StorageTargetId equals target.Id
                where enabledDatabaseIds.Contains(backup.DatabaseEndpointId) &&
                      backup.Status == BackupStatus.Succeeded &&
                      replica.Status == ReplicaStatus.Succeeded &&
                      (target.Type != StorageProviderType.OdinVaultReplica ||
                       (replica.ContentHashSha256 != null &&
                        replica.ContentHashSha256.Length == 64))
                select backup.DatabaseEndpointId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var protectedDatabaseIds = locallyProtectedDatabaseIds
                .Concat(replicaProtectedDatabaseIds)
                .Distinct()
                .ToHashSet();

            var databasesWithoutSuccessfulBackup = enabledDatabaseIds
                .Count(x => !protectedDatabaseIds.Contains(x));

            var storageFreeBytes = TryGetAvailableFreeSpace(paths.StorageDirectory);
            var replicaFreeBytes = TryGetAvailableFreeSpace(paths.ReplicaDirectory);

            var degraded =
                staleRunningJobs > 0 ||
                failedJobsLast24Hours > 0 ||
                failedReplicasDue > 0 ||
                databasesWithoutSuccessfulBackup > 0 ||
                queuedJobs >= 60 ||
                storageFreeBytes is < 512L * 1024 * 1024 ||
                replicaFreeBytes is < 512L * 1024 * 1024;

            return new AgentHealthSnapshot(
                "healthy",
                degraded ? "degraded" : "healthy",
                now,
                true,
                enabledDatabases,
                queuedJobs,
                runningJobs,
                staleRunningJobs,
                failedJobsLast24Hours,
                failedReplicasDue,
                databasesWithoutSuccessfulBackup,
                storageFreeBytes,
                replicaFreeBytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent health check failed while reading operational state.");
            return Unavailable(now);
        }
    }

    private AgentHealthSnapshot Unavailable(DateTime now) => new(
        "unhealthy",
        "unknown",
        now,
        false,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        TryGetAvailableFreeSpace(paths.StorageDirectory),
        TryGetAvailableFreeSpace(paths.ReplicaDirectory));

    private static long? TryGetAvailableFreeSpace(string directory)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            return string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
