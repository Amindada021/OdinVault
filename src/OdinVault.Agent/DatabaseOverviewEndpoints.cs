using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;

namespace OdinVault.Agent;

public static class DatabaseOverviewEndpoints
{
    public static void MapDatabaseOverviewEndpoints(this WebApplication app)
    {
        app.MapGet("/api/databases/overview", async (
            OdinVaultDbContext db,
            CancellationToken ct) =>
        {
            var databases = await db.DatabaseEndpoints
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Host,
                    x.Port,
                    x.DatabaseName,
                    x.IsEnabled
                })
                .ToListAsync(ct);

            var databaseIds = databases.Select(x => x.Id).ToList();

            var backups = await db.BackupRecords
                .AsNoTracking()
                .Where(x => databaseIds.Contains(x.DatabaseEndpointId))
                .OrderByDescending(x => x.StartedAtUtc)
                .ToListAsync(ct);

            var successfulReplicaBackupIds = await db.BackupReplicas
                .AsNoTracking()
                .Where(x => x.Status == ReplicaStatus.Succeeded)
                .Select(x => x.BackupRecordId)
                .Distinct()
                .ToListAsync(ct);

            var result = databases.Select(database =>
            {
                var databaseBackups = backups
                    .Where(x => x.DatabaseEndpointId == database.Id)
                    .OrderByDescending(x => x.StartedAtUtc)
                    .ToList();

                var latest = databaseBackups.FirstOrDefault();
                var latestUsable = databaseBackups.FirstOrDefault(x =>
                    x.Status == BackupStatus.Succeeded &&
                    (x.LocalFileAvailable || successfulReplicaBackupIds.Contains(x.Id)));

                return new
                {
                    database.Id,
                    database.Name,
                    database.Host,
                    database.Port,
                    database.DatabaseName,
                    database.IsEnabled,
                    isProtected = latestUsable is not null,
                    latestBackupAtUtc = latest?.CompletedAtUtc ?? latest?.StartedAtUtc,
                    latestBackupStatus = latest is null ? (int?)null : (int)latest.Status,
                    latestVerificationStatus = latest is null ? (int?)null : (int)latest.VerificationStatus,
                    latestBackupSizeBytes = latest?.SizeBytes
                };
            });

            return Results.Ok(result);
        });

        app.MapGet("/api/databases/{id:guid}/details", async (
            Guid id,
            OdinVaultDbContext db,
            CancellationToken ct) =>
        {
            var database = await db.DatabaseEndpoints
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (database is null)
                return Results.NotFound(new { message = "Database endpoint was not found." });

            var policy = await db.BackupPolicies
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.DatabaseEndpointId == id, ct);

            var backups = await db.BackupRecords
                .AsNoTracking()
                .Where(x => x.DatabaseEndpointId == id)
                .OrderByDescending(x => x.StartedAtUtc)
                .Take(60)
                .ToListAsync(ct);

            var backupIds = backups.Select(x => x.Id).ToList();

            var replicas = await (
                from replica in db.BackupReplicas.AsNoTracking()
                join target in db.StorageTargets.AsNoTracking()
                    on replica.StorageTargetId equals target.Id
                where backupIds.Contains(replica.BackupRecordId)
                orderby replica.StartedAtUtc descending
                select new
                {
                    replica.BackupRecordId,
                    replica.StorageTargetId,
                    target.Name,
                    target.Type,
                    replica.Status,
                    replica.SizeBytes,
                    replica.ContentHashSha256,
                    replica.Error,
                    replica.StartedAtUtc,
                    replica.CompletedAtUtc
                })
                .ToListAsync(ct);

            var latest = backups.FirstOrDefault();
            var latestReplicaRows = latest is null
                ? []
                : replicas
                    .Where(x => x.BackupRecordId == latest.Id)
                    .ToList();

            var usableBackup = backups.FirstOrDefault(x =>
                x.Status == BackupStatus.Succeeded &&
                (x.LocalFileAvailable ||
                 replicas.Any(replica =>
                     replica.BackupRecordId == x.Id &&
                     replica.Status == ReplicaStatus.Succeeded)));

            return Results.Ok(new
            {
                database = new
                {
                    database.Id,
                    database.Name,
                    database.Host,
                    database.Port,
                    database.DatabaseName,
                    database.IsEnabled,
                    policy = policy is null ? null : new
                    {
                        policy.ScheduleCron,
                        policy.MaxLocalBackups,
                        policy.VerifyAfterBackup,
                        policy.BackupDirectory,
                        policy.IsEnabled
                    }
                },
                protection = new
                {
                    isProtected = usableBackup is not null,
                    latestBackupAtUtc = latest?.CompletedAtUtc ?? latest?.StartedAtUtc,
                    latestBackupStatus = latest is null ? (int?)null : (int)latest.Status,
                    latestVerificationStatus = latest is null ? (int?)null : (int)latest.VerificationStatus,
                    latestBackupSizeBytes = latest?.SizeBytes,
                    latestReplicaSucceeded = latestReplicaRows.Count(x => x.Status == ReplicaStatus.Succeeded),
                    latestReplicaTotal = latestReplicaRows.Count
                },
                backups = backups.Select(x => new
                {
                    x.Id,
                    x.Status,
                    x.VerificationStatus,
                    x.SizeBytes,
                    x.StartedAtUtc,
                    x.CompletedAtUtc,
                    x.LocalFileAvailable,
                    x.Error
                }),
                replicas
            });
        });
    }
}
