using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;

namespace OdinVault.Agent;

public static class AlertEndpoints
{
    public static void MapAlertEndpoints(this WebApplication app)
    {
        app.MapGet("/api/alerts", async (
            bool? includeRead,
            OdinVaultDbContext db,
            AgentHealthService healthService,
            CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var cutoff = now.AddDays(-30);
            var alerts = new List<AlertItem>();

            var databaseNames = await db.DatabaseEndpoints
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

            var failedBackups = await db.BackupRecords
                .AsNoTracking()
                .Where(x => x.Status == BackupStatus.Failed &&
                            x.CompletedAtUtc != null &&
                            x.CompletedAtUtc >= cutoff)
                .OrderByDescending(x => x.CompletedAtUtc)
                .Take(100)
                .ToListAsync(ct);

            foreach (var backup in failedBackups)
            {
                alerts.Add(new AlertItem(
                    $"backup-failed:{backup.Id:N}",
                    "critical",
                    "backup",
                    backup.DatabaseEndpointId,
                    databaseNames.GetValueOrDefault(backup.DatabaseEndpointId, "Database"),
                    "بکاپ ناموفق",
                    string.IsNullOrWhiteSpace(backup.Error)
                        ? "آخرین عملیات بکاپ کامل نشد."
                        : backup.Error,
                    backup.CompletedAtUtc ?? backup.StartedAtUtc));
            }

            var verifyFailures = await db.BackupRecords
                .AsNoTracking()
                .Where(x => x.Status == BackupStatus.Succeeded &&
                            x.VerificationStatus == VerificationStatus.Failed &&
                            x.CompletedAtUtc != null &&
                            x.CompletedAtUtc >= cutoff)
                .OrderByDescending(x => x.CompletedAtUtc)
                .Take(100)
                .ToListAsync(ct);

            foreach (var backup in verifyFailures)
            {
                alerts.Add(new AlertItem(
                    $"verify-failed:{backup.Id:N}",
                    "warning",
                    "verify",
                    backup.DatabaseEndpointId,
                    databaseNames.GetValueOrDefault(backup.DatabaseEndpointId, "Database"),
                    "بررسی سلامت بکاپ ناموفق",
                    string.IsNullOrWhiteSpace(backup.Error)
                        ? "فایل بکاپ ساخته شده اما Verify ناموفق بوده است."
                        : backup.Error,
                    backup.CompletedAtUtc ?? backup.StartedAtUtc));
            }

            var replicaFailures = await (
                from replica in db.BackupReplicas.AsNoTracking()
                join backup in db.BackupRecords.AsNoTracking()
                    on replica.BackupRecordId equals backup.Id
                join target in db.StorageTargets.AsNoTracking()
                    on replica.StorageTargetId equals target.Id
                where replica.Status == ReplicaStatus.Failed &&
                      replica.CompletedAtUtc != null &&
                      replica.CompletedAtUtc >= cutoff
                orderby replica.CompletedAtUtc descending
                select new
                {
                    replica.Id,
                    backup.DatabaseEndpointId,
                    TargetName = target.Name,
                    replica.Error,
                    replica.CompletedAtUtc
                })
                .Take(100)
                .ToListAsync(ct);

            foreach (var replica in replicaFailures)
            {
                alerts.Add(new AlertItem(
                    $"replica-failed:{replica.Id:N}",
                    "warning",
                    "replica",
                    replica.DatabaseEndpointId,
                    databaseNames.GetValueOrDefault(replica.DatabaseEndpointId, "Database"),
                    $"Replica ناموفق: {replica.TargetName}",
                    string.IsNullOrWhiteSpace(replica.Error)
                        ? "ارسال نسخه ثانویه کامل نشده است."
                        : replica.Error,
                    replica.CompletedAtUtc ?? now));
            }

            var staleCutoff = now.AddHours(-6);
            var staleJobs = await db.BackupJobs
                .AsNoTracking()
                .Where(x => x.Status == BackupJobStatus.Running &&
                            x.UpdatedAtUtc < staleCutoff)
                .OrderBy(x => x.UpdatedAtUtc)
                .Take(50)
                .ToListAsync(ct);

            foreach (var job in staleJobs)
            {
                alerts.Add(new AlertItem(
                    $"stale-job:{job.Id:N}",
                    "critical",
                    "job",
                    job.DatabaseEndpointId,
                    databaseNames.GetValueOrDefault(job.DatabaseEndpointId, "Database"),
                    "Job طولانی یا گیرکرده",
                    $"Job بیش از ۶ ساعت در وضعیت Running مانده است. مرحله فعلی: {job.Stage}",
                    job.UpdatedAtUtc));
            }

            var enabledDatabaseIds = await db.DatabaseEndpoints
                .AsNoTracking()
                .Where(x => x.IsEnabled)
                .Select(x => x.Id)
                .ToListAsync(ct);

            var locallyProtectedIds = await db.BackupRecords
                .AsNoTracking()
                .Where(x => enabledDatabaseIds.Contains(x.DatabaseEndpointId) &&
                            x.Status == BackupStatus.Succeeded &&
                            x.LocalFileAvailable)
                .Select(x => x.DatabaseEndpointId)
                .Distinct()
                .ToListAsync(ct);

            var replicaProtectedIds = await (
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
                .ToListAsync(ct);

            var protectedIds = locallyProtectedIds
                .Concat(replicaProtectedIds)
                .Distinct()
                .ToHashSet();

            foreach (var databaseId in enabledDatabaseIds.Where(x => !protectedIds.Contains(x)))
            {
                alerts.Add(new AlertItem(
                    $"db-unprotected:{databaseId:N}",
                    "critical",
                    "protection",
                    databaseId,
                    databaseNames.GetValueOrDefault(databaseId, "Database"),
                    "دیتابیس بدون نسخه محافظت‌شده",
                    "هیچ بکاپ موفق و قابل استفاده‌ای برای این دیتابیس وجود ندارد.",
                    now));
            }

            var health = await healthService.CheckAsync(ct);
            const long lowDiskThreshold = 512L * 1024 * 1024;
            if (health.StorageFreeBytes is < lowDiskThreshold)
            {
                alerts.Add(new AlertItem(
                    "storage-low:local",
                    "critical",
                    "storage",
                    null,
                    "Local Storage",
                    "فضای آزاد بکاپ کم است",
                    $"فضای آزاد مقصد Local کمتر از ۵۱۲ مگابایت است: {FormatBytes(health.StorageFreeBytes)}",
                    now));
            }

            if (health.ProtectionStatus == "degraded" &&
                alerts.All(x => x.Category != "protection" && x.Category != "storage" && x.Category != "job"))
            {
                alerts.Add(new AlertItem(
                    "agent-health:degraded",
                    "warning",
                    "health",
                    null,
                    "OdinVault Agent",
                    "وضعیت حفاظت نیاز به بررسی دارد",
                    "Agent فعال است اما حداقل یکی از شاخص‌های حفاظت در وضعیت degraded قرار دارد.",
                    now));
            }

            var readKeys = await db.AlertReadStates
                .AsNoTracking()
                .Select(x => x.AlertKey)
                .ToHashSetAsync(ct);

            var result = alerts
                .GroupBy(x => x.Key)
                .Select(x => x.OrderByDescending(y => y.OccurredAtUtc).First())
                .Select(x => new
                {
                    key = x.Key,
                    severity = x.Severity,
                    category = x.Category,
                    databaseId = x.DatabaseId,
                    databaseName = x.DatabaseName,
                    title = x.Title,
                    message = x.Message,
                    occurredAtUtc = x.OccurredAtUtc,
                    isRead = readKeys.Contains(x.Key)
                })
                .Where(x => includeRead == true || !x.isRead)
                .OrderBy(x => x.severity == "critical" ? 0 : x.severity == "warning" ? 1 : 2)
                .ThenByDescending(x => x.occurredAtUtc)
                .Take(250)
                .ToArray();

            return Results.Ok(new
            {
                utc = now,
                unreadCount = result.Count(x => !x.isRead),
                alerts = result
            });
        });

        app.MapPost("/api/alerts/mark-read", async (
            MarkAlertsReadRequest request,
            OdinVaultDbContext db,
            CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var keys = (request.Keys ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => x.Length <= 300)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (keys.Length == 0)
                return Results.BadRequest(new { message = "At least one alert key is required." });

            var existing = await db.AlertReadStates
                .Where(x => keys.Contains(x.AlertKey))
                .ToDictionaryAsync(x => x.AlertKey, ct);

            foreach (var key in keys)
            {
                if (existing.TryGetValue(key, out var state))
                {
                    state.ReadAtUtc = now;
                }
                else
                {
                    db.AlertReadStates.Add(new AlertReadState
                    {
                        AlertKey = key,
                        ReadAtUtc = now
                    });
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { read = keys.Length, readAtUtc = now });
        });
    }

    private static string FormatBytes(long? value)
    {
        if (value is null) return "نامشخص";
        var bytes = value.Value;
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024d:0.0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):0.0} MB";
        return $"{bytes / (1024d * 1024 * 1024):0.00} GB";
    }

    private sealed record AlertItem(
        string Key,
        string Severity,
        string Category,
        Guid? DatabaseId,
        string DatabaseName,
        string Title,
        string Message,
        DateTime OccurredAtUtc);
}

public sealed record MarkAlertsReadRequest(IReadOnlyList<string>? Keys);
