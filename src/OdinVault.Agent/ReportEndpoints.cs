using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;

namespace OdinVault.Agent;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        app.MapGet("/api/reports/backup", async (
            int? days,
            OdinVaultDbContext db,
            AgentHealthService healthService,
            CancellationToken ct) =>
        {
            var rangeDays = days switch
            {
                7 => 7,
                90 => 90,
                _ => 30
            };

            var toUtc = DateTime.UtcNow;
            var fromUtc = toUtc.Date.AddDays(-(rangeDays - 1));

            var databases = await db.DatabaseEndpoints
                .AsNoTracking()
                .Where(x => x.IsEnabled)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name })
                .ToListAsync(ct);

            var databaseIds = databases.Select(x => x.Id).ToList();

            var backups = await db.BackupRecords
                .AsNoTracking()
                .Where(x => databaseIds.Contains(x.DatabaseEndpointId) &&
                            x.StartedAtUtc >= fromUtc &&
                            x.StartedAtUtc <= toUtc)
                .ToListAsync(ct);

            var backupIds = backups.Select(x => x.Id).ToList();

            var replicas = await db.BackupReplicas
                .AsNoTracking()
                .Where(x => backupIds.Contains(x.BackupRecordId))
                .ToListAsync(ct);

            var succeeded = backups.Where(x => x.Status == BackupStatus.Succeeded).ToList();
            var failed = backups.Where(x => x.Status == BackupStatus.Failed).ToList();
            var verifyFailed = backups.Where(x =>
                x.Status == BackupStatus.Succeeded &&
                x.VerificationStatus == VerificationStatus.Failed).ToList();

            var durations = succeeded
                .Where(x => x.CompletedAtUtc.HasValue)
                .Select(x => Math.Max(0, (x.CompletedAtUtc!.Value - x.StartedAtUtc).TotalSeconds))
                .ToList();

            var health = await healthService.CheckAsync(ct);

            var daily = Enumerable.Range(0, rangeDays)
                .Select(offset =>
                {
                    var date = fromUtc.Date.AddDays(offset);
                    var next = date.AddDays(1);
                    var bucket = backups
                        .Where(x => x.StartedAtUtc >= date && x.StartedAtUtc < next)
                        .ToList();

                    var successfulBucket = bucket
                        .Where(x => x.Status == BackupStatus.Succeeded)
                        .ToList();

                    var durationBucket = successfulBucket
                        .Where(x => x.CompletedAtUtc.HasValue)
                        .Select(x => Math.Max(0, (x.CompletedAtUtc!.Value - x.StartedAtUtc).TotalSeconds))
                        .ToList();

                    return new
                    {
                        dateUtc = date,
                        succeeded = successfulBucket.Count,
                        failed = bucket.Count(x => x.Status == BackupStatus.Failed),
                        verifyFailed = successfulBucket.Count(x => x.VerificationStatus == VerificationStatus.Failed),
                        totalSizeBytes = successfulBucket.Sum(x => x.SizeBytes ?? 0L),
                        averageDurationSeconds = durationBucket.Count == 0
                            ? (double?)null
                            : durationBucket.Average()
                    };
                })
                .ToArray();

            var databaseRows = databases.Select(database =>
            {
                var rows = backups
                    .Where(x => x.DatabaseEndpointId == database.Id)
                    .OrderByDescending(x => x.StartedAtUtc)
                    .ToList();

                var successRows = rows
                    .Where(x => x.Status == BackupStatus.Succeeded)
                    .OrderByDescending(x => x.StartedAtUtc)
                    .ToList();

                var latest = rows.FirstOrDefault();
                var latestSuccess = successRows.FirstOrDefault();
                var previousSuccess = successRows.Skip(1).FirstOrDefault();

                var rowDurations = successRows
                    .Where(x => x.CompletedAtUtc.HasValue)
                    .Select(x => Math.Max(0, (x.CompletedAtUtc!.Value - x.StartedAtUtc).TotalSeconds))
                    .ToList();

                var replicaFailures = replicas.Count(replica =>
                    replica.Status == ReplicaStatus.Failed &&
                    rows.Any(backup => backup.Id == replica.BackupRecordId));

                double? growthPercent = null;
                if (latestSuccess?.SizeBytes is > 0 &&
                    previousSuccess?.SizeBytes is > 0)
                {
                    growthPercent =
                        (latestSuccess.SizeBytes.Value - previousSuccess.SizeBytes.Value) /
                        (double)previousSuccess.SizeBytes.Value * 100d;
                }

                var total = rows.Count;
                var successCount = successRows.Count;
                var failedCount = rows.Count(x => x.Status == BackupStatus.Failed);
                var verifyFailureCount = rows.Count(x =>
                    x.Status == BackupStatus.Succeeded &&
                    x.VerificationStatus == VerificationStatus.Failed);

                var severity = latest is null || successCount == 0
                    ? "critical"
                    : latest.Status == BackupStatus.Failed
                        ? "critical"
                        : verifyFailureCount > 0 || replicaFailures > 0
                            ? "warning"
                            : "healthy";

                return new
                {
                    databaseId = database.Id,
                    databaseName = database.Name,
                    totalBackups = total,
                    succeeded = successCount,
                    failed = failedCount,
                    verifyFailed = verifyFailureCount,
                    replicaFailed = replicaFailures,
                    successRate = total == 0
                        ? (double?)null
                        : successCount / (double)total * 100d,
                    averageDurationSeconds = rowDurations.Count == 0
                        ? (double?)null
                        : rowDurations.Average(),
                    latestBackupAtUtc = latest?.CompletedAtUtc ?? latest?.StartedAtUtc,
                    latestBackupStatus = latest is null ? (int?)null : (int)latest.Status,
                    latestSizeBytes = latestSuccess?.SizeBytes,
                    previousSizeBytes = previousSuccess?.SizeBytes,
                    sizeGrowthPercent = growthPercent,
                    severity
                };
            })
            .OrderBy(x => x.severity == "critical" ? 0 : x.severity == "warning" ? 1 : 2)
            .ThenBy(x => x.databaseName)
            .ToArray();

            var totalBackups = backups.Count;
            var successRate = totalBackups == 0
                ? (double?)null
                : succeeded.Count / (double)totalBackups * 100d;

            return Results.Ok(new
            {
                rangeDays,
                fromUtc,
                toUtc,
                summary = new
                {
                    totalBackups,
                    succeeded = succeeded.Count,
                    failed = failed.Count,
                    verifyFailed = verifyFailed.Count,
                    replicaFailed = replicas.Count(x => x.Status == ReplicaStatus.Failed),
                    successRate,
                    averageDurationSeconds = durations.Count == 0
                        ? (double?)null
                        : durations.Average(),
                    totalSuccessfulBytes = succeeded.Sum(x => x.SizeBytes ?? 0L),
                    enabledDatabases = health.EnabledDatabases,
                    protectedDatabases = Math.Max(
                        0,
                        health.EnabledDatabases - health.DatabasesWithoutSuccessfulBackup),
                    storageFreeBytes = health.StorageFreeBytes
                },
                daily,
                databases = databaseRows
            });
        });
    }
}
