using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;

namespace OdinVault.Agent;

public static class BackupOverviewEndpoints
{
    public static void MapBackupOverviewEndpoints(this WebApplication app)
    {
        app.MapGet("/api/backups/overview", async (
            int? take,
            DateTime? beforeUtc,
            OdinVaultDbContext db,
            CancellationToken ct) =>
        {
            var limit = Math.Clamp(take ?? 200, 20, 500);

            var databases = await db.DatabaseEndpoints
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name })
                .ToListAsync(ct);

            var databaseNames = databases.ToDictionary(x => x.Id, x => x.Name);

            var jobsQuery = db.BackupJobs.AsNoTracking();
            var backupsQuery = db.BackupRecords.AsNoTracking();
            if (beforeUtc is DateTime cursor)
            {
                jobsQuery = jobsQuery.Where(x => x.CreatedAtUtc < cursor);
                backupsQuery = backupsQuery.Where(x => x.StartedAtUtc < cursor);
            }

            var jobs = await jobsQuery.OrderByDescending(x => x.CreatedAtUtc).Take(limit).ToListAsync(ct);
            var backups = await backupsQuery.OrderByDescending(x => x.StartedAtUtc).Take(limit).ToListAsync(ct);

            var jobItems = jobs.Select(x => new
            {
                x.Id,
                x.DatabaseEndpointId,
                databaseName = databaseNames.GetValueOrDefault(x.DatabaseEndpointId, "Database"),
                status = (int)x.Status,
                x.Stage,
                x.Percent,
                x.CreatedAtUtc,
                x.StartedAtUtc,
                x.UpdatedAtUtc,
                x.CompletedAtUtc,
                x.BackupRecordId,
                x.ErrorCode,
                x.ErrorMessage
            }).ToArray();

            var backupItems = backups.Select(x => new
            {
                x.Id,
                x.DatabaseEndpointId,
                databaseName = databaseNames.GetValueOrDefault(x.DatabaseEndpointId, "Database"),
                status = (int)x.Status,
                verificationStatus = (int)x.VerificationStatus,
                x.SizeBytes,
                x.StartedAtUtc,
                x.CompletedAtUtc,
                x.LocalFileAvailable,
                x.Error
            }).ToArray();

            return Results.Ok(new
            {
                utc = DateTime.UtcNow,
                nextBeforeUtc = jobs.Concat<object>(backups).Any() ? new[] { jobs.LastOrDefault()?.CreatedAtUtc, backups.LastOrDefault()?.StartedAtUtc }.Where(x => x.HasValue).Min() : null,
                jobs = jobItems,
                backups = backupItems
            });
        });
    }
}
