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

            var jobs = await db.BackupJobs
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(limit)
                .ToListAsync(ct);

            var backups = await db.BackupRecords
                .AsNoTracking()
                .OrderByDescending(x => x.StartedAtUtc)
                .Take(limit)
                .ToListAsync(ct);

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
                jobs = jobItems,
                backups = backupItems
            });
        });
    }
}
