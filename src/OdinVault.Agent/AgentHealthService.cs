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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent health check could not connect to SQLite.");
            return Unavailable(now);
        }

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

        var protectedDatabaseIds = await db.BackupRecords
            .AsNoTracking()
            .Where(x => enabledDatabaseIds.Contains(x.DatabaseEndpointId) &&
                        x.Status == BackupStatus.Succeeded &&
                        x.LocalFileAvailable)
            .Select(x => x.DatabaseEndpointId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var databasesWithoutSuccessfulBackup = enabledDatabaseIds
            .Except(protectedDatabaseIds)
            .Count();

        var storageFreeBytes = TryGetAvailableFreeSpace(paths.StorageDirectory);
        var replicaFreeBytes = TryGetAvailableFreeSpace(paths.ReplicaDirectory);

        var degraded =
            staleRunningJobs > 0 ||
            failedJobsLast24Hours > 0 ||
            failedReplicasDue > 0 ||
            queuedJobs >= 60 ||
            storageFreeBytes is < 512L * 1024 * 1024 ||
            replicaFreeBytes is < 512L * 1024 * 1024;

        return new AgentHealthSnapshot(
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

    private AgentHealthSnapshot Unavailable(DateTime now) => new(
        "unhealthy",
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
