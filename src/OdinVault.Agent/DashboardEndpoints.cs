using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;

namespace OdinVault.Agent;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        app.MapGet("/api/dashboard/stats", async (
            int? days,
            OdinVaultDbContext db,
            CancellationToken ct) =>
        {
            var rangeDays = days == 7 ? 7 : 30;
            var todayUtc = DateTime.UtcNow.Date;
            var fromUtc = todayUtc.AddDays(-(rangeDays - 1));

            var databases = await db.DatabaseEndpoints
                .AsNoTracking()
                .Where(x => x.IsEnabled)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name })
                .ToListAsync(ct);

            var databaseIds = databases.Select(x => x.Id).ToList();
            var records = await db.BackupRecords
                .AsNoTracking()
                .Where(x => databaseIds.Contains(x.DatabaseEndpointId) &&
                            x.StartedAtUtc >= fromUtc)
                .Select(x => new
                {
                    x.Id,
                    x.DatabaseEndpointId,
                    x.Status,
                    x.SizeBytes,
                    x.StartedAtUtc,
                    x.CompletedAtUtc
                })
                .ToListAsync(ct);

            var daily = Enumerable.Range(0, rangeDays)
                .Select(offset =>
                {
                    var date = fromUtc.AddDays(offset);
                    var next = date.AddDays(1);
                    var bucket = records.Where(x =>
                        x.StartedAtUtc >= date &&
                        x.StartedAtUtc < next).ToList();

                    var succeeded = bucket
                        .Where(x => x.Status == BackupStatus.Succeeded)
                        .ToList();

                    var durations = succeeded
                        .Where(x => x.CompletedAtUtc.HasValue)
                        .Select(x => Math.Max(
                            0,
                            (x.CompletedAtUtc!.Value - x.StartedAtUtc).TotalSeconds))
                        .ToList();

                    return new
                    {
                        dateUtc = date,
                        succeeded = succeeded.Count,
                        failed = bucket.Count(x => x.Status == BackupStatus.Failed),
                        totalSizeBytes = succeeded.Sum(x => x.SizeBytes ?? 0L),
                        averageDurationSeconds = durations.Count == 0
                            ? (double?)null
                            : durations.Average()
                    };
                })
                .ToArray();

            var latestDatabaseSizes = databases
                .Select(database =>
                {
                    var latest = records
                        .Where(x => x.DatabaseEndpointId == database.Id &&
                                    x.Status == BackupStatus.Succeeded &&
                                    x.SizeBytes.HasValue)
                        .OrderByDescending(x => x.StartedAtUtc)
                        .FirstOrDefault();

                    return new
                    {
                        databaseId = database.Id,
                        databaseName = database.Name,
                        sizeBytes = latest?.SizeBytes
                    };
                })
                .Where(x => x.sizeBytes.HasValue)
                .OrderByDescending(x => x.sizeBytes)
                .ToArray();

            return Results.Ok(new
            {
                rangeDays,
                fromUtc,
                toUtc = todayUtc.AddDays(1),
                daily,
                databaseSizes = latestDatabaseSizes
            });
        });

        app.MapGet("/api/dashboard", async (
            OdinVaultDbContext db,
            AgentHealthService healthService,
            CancellationToken ct) =>
        {
            var health = await healthService.CheckAsync(ct);
            var now = DateTime.UtcNow;
            var failedCutoff = now.AddHours(-24);

            var databases = await db.DatabaseEndpoints
                .AsNoTracking()
                .Where(x => x.IsEnabled)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name })
                .ToListAsync(ct);

            var databaseIds = databases.Select(x => x.Id).ToList();

            var recentBackups = await db.BackupRecords
                .AsNoTracking()
                .Where(x => databaseIds.Contains(x.DatabaseEndpointId))
                .OrderByDescending(x => x.StartedAtUtc)
                .Take(200)
                .ToListAsync(ct);

            var successfulReplicaBackupIds = await db.BackupReplicas
                .AsNoTracking()
                .Where(x => x.Status == ReplicaStatus.Succeeded)
                .Select(x => x.BackupRecordId)
                .Distinct()
                .ToListAsync(ct);

            var recentReplicaFailures = await (
                from replica in db.BackupReplicas.AsNoTracking()
                join backup in db.BackupRecords.AsNoTracking()
                    on replica.BackupRecordId equals backup.Id
                join database in db.DatabaseEndpoints.AsNoTracking()
                    on backup.DatabaseEndpointId equals database.Id
                join target in db.StorageTargets.AsNoTracking()
                    on replica.StorageTargetId equals target.Id
                where database.IsEnabled &&
                      replica.Status == ReplicaStatus.Failed
                orderby replica.CompletedAtUtc descending
                select new
                {
                    database.Id,
                    DatabaseName = database.Name,
                    TargetName = target.Name,
                    replica.Error,
                    replica.CompletedAtUtc
                })
                .Take(20)
                .ToListAsync(ct);

            var attention = new List<DashboardAttentionItem>();

            foreach (var database in databases)
            {
                var backups = recentBackups
                    .Where(x => x.DatabaseEndpointId == database.Id)
                    .OrderByDescending(x => x.StartedAtUtc)
                    .ToList();

                var usableSuccessfulBackup = backups.FirstOrDefault(x =>
                    x.Status == BackupStatus.Succeeded &&
                    (x.LocalFileAvailable || successfulReplicaBackupIds.Contains(x.Id)));

                if (usableSuccessfulBackup is null)
                {
                    attention.Add(new DashboardAttentionItem(
                        "critical",
                        database.Id,
                        database.Name,
                        "بدون بکاپ قابل استفاده",
                        "برای این دیتابیس هنوز نسخه بکاپ موفق و قابل استفاده ثبت نشده است.",
                        null));
                    continue;
                }

                var latest = backups.FirstOrDefault();
                if (latest?.Status == BackupStatus.Failed)
                {
                    attention.Add(new DashboardAttentionItem(
                        "critical",
                        database.Id,
                        database.Name,
                        "آخرین بکاپ ناموفق",
                        string.IsNullOrWhiteSpace(latest.Error)
                            ? "آخرین تلاش برای بکاپ ناموفق بوده است."
                            : latest.Error,
                        latest.CompletedAtUtc ?? latest.StartedAtUtc));
                }
                else if (latest?.Status == BackupStatus.Succeeded &&
                         latest.VerificationStatus == VerificationStatus.Failed)
                {
                    attention.Add(new DashboardAttentionItem(
                        "warning",
                        database.Id,
                        database.Name,
                        "بررسی سلامت ناموفق",
                        string.IsNullOrWhiteSpace(latest.Error)
                            ? "فایل بکاپ ساخته شده ولی Verify ناموفق بوده است."
                            : latest.Error,
                        latest.CompletedAtUtc ?? latest.StartedAtUtc));
                }
            }

            foreach (var replica in recentReplicaFailures)
            {
                attention.Add(new DashboardAttentionItem(
                    "warning",
                    replica.Id,
                    replica.DatabaseName,
                    $"Replica ناموفق: {replica.TargetName}",
                    string.IsNullOrWhiteSpace(replica.Error)
                        ? "ارسال نسخه ثانویه کامل نشده است."
                        : replica.Error,
                    replica.CompletedAtUtc));
            }

            var recentActivity = recentBackups
                .Take(12)
                .Select(backup =>
                {
                    var database = databases.FirstOrDefault(x => x.Id == backup.DatabaseEndpointId);
                    return new
                    {
                        databaseId = backup.DatabaseEndpointId,
                        databaseName = database?.Name ?? "Database",
                        backupId = backup.Id,
                        status = (int)backup.Status,
                        verificationStatus = (int)backup.VerificationStatus,
                        backup.SizeBytes,
                        backup.StartedAtUtc,
                        backup.CompletedAtUtc,
                        backup.Error
                    };
                })
                .ToList();

            return Results.Ok(new
            {
                utc = now,
                status = health.Status,
                protectionStatus = health.ProtectionStatus,
                enabledDatabases = health.EnabledDatabases,
                protectedDatabases = Math.Max(0, health.EnabledDatabases - health.DatabasesWithoutSuccessfulBackup),
                activeJobs = health.QueuedJobs + health.RunningJobs,
                failedJobsLast24Hours = health.FailedJobsLast24Hours,
                storageFreeBytes = health.StorageFreeBytes,
                attention = attention
                    .OrderBy(x => x.Severity == "critical" ? 0 : 1)
                    .ThenByDescending(x => x.OccurredAtUtc)
                    .Take(10)
                    .ToArray(),
                recentActivity
            });
        });
    }
}


internal sealed record DashboardAttentionItem(
    string Severity,
    Guid DatabaseId,
    string DatabaseName,
    string Title,
    string Message,
    DateTime? OccurredAtUtc);
