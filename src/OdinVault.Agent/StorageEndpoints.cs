using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;
using OdinVault.Storage.GoogleDrive;

namespace OdinVault.Agent;

public static class StorageEndpoints
{
    public static void MapStorageEndpoints(this WebApplication app)
    {
        app.MapGet("/api/storage-targets", async (OdinVaultDbContext db, CancellationToken ct) =>
        {
            var items = await db.StorageTargets
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Type,
                    x.IsEnabled,
                    x.FolderId,
                    x.AccountEmail,
                    isConnected = x.ProtectedRefreshToken != null,
                    x.CreatedAtUtc
                })
                .ToListAsync(ct);
            return Results.Ok(items);
        });

        app.MapPost("/api/storage-targets/google-drive/pair/start", (
            StartGoogleDrivePairingRequest request,
            GoogleDriveOAuthOptions options,
            GoogleDriveOAuthService oauth,
            GoogleDrivePairingStateStore stateStore) =>
        {
            if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
                return Results.BadRequest(new { message = "Google Drive OAuth is not configured on this Agent." });
            if (string.IsNullOrWhiteSpace(request.TargetName) || string.IsNullOrWhiteSpace(request.RedirectUri))
                return Results.BadRequest(new { message = "targetName and redirectUri are required." });

            var pairing = stateStore.Create(request.TargetName.Trim(), request.RedirectUri.Trim(), request.FolderId);
            var url = oauth.BuildAuthorizationUrl(pairing.RedirectUri, pairing.State);
            return Results.Ok(new
            {
                authorizationUrl = url,
                pairing.State,
                pairing.ExpiresAtUtc
            });
        });

        app.MapPost("/api/storage-targets/google-drive/pair/complete", async (
            CompleteGoogleDrivePairingRequest request,
            GoogleDrivePairingStateStore stateStore,
            GoogleDriveOAuthService oauth,
            ISecretProtector protector,
            OdinVaultDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.State) || string.IsNullOrWhiteSpace(request.Code))
                return Results.BadRequest(new { message = "state and code are required." });
            if (!stateStore.TryConsume(request.State, out var pairing))
                return Results.BadRequest(new { message = "OAuth pairing state is invalid or expired." });

            var token = await oauth.ExchangeCodeAsync(request.Code, pairing.RedirectUri, ct);
            var target = new StorageTarget
            {
                Name = pairing.TargetName,
                Type = StorageProviderType.GoogleDrive,
                FolderId = pairing.FolderId,
                AccountEmail = string.IsNullOrWhiteSpace(request.AccountEmail) ? null : request.AccountEmail.Trim(),
                ProtectedRefreshToken = protector.Protect(token.RefreshToken),
                IsEnabled = true
            };

            db.StorageTargets.Add(target);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/storage-targets/{target.Id}", new
            {
                target.Id,
                target.Name,
                target.Type,
                target.FolderId,
                target.AccountEmail,
                target.IsEnabled
            });
        });

        app.MapPut("/api/storage-targets/{id:guid}", async (
            Guid id,
            UpdateStorageTargetRequest request,
            OdinVaultDbContext db,
            CancellationToken ct) =>
        {
            var target = await db.StorageTargets.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (target is null)
                return Results.NotFound(new { message = "Storage target was not found." });
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { message = "name is required." });

            target.Name = request.Name.Trim();
            target.FolderId = string.IsNullOrWhiteSpace(request.FolderId) ? null : request.FolderId.Trim();
            target.IsEnabled = request.IsEnabled;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { target.Id, target.Name, target.Type, target.FolderId, target.AccountEmail, target.IsEnabled });
        });

        app.MapDelete("/api/storage-targets/{id:guid}", async (
            Guid id,
            OdinVaultDbContext db,
            CancellationToken ct) =>
        {
            var target = await db.StorageTargets.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (target is null)
                return Results.NotFound(new { message = "Storage target was not found." });

            if (await db.BackupReplicas.AnyAsync(x => x.StorageTargetId == id, ct))
                return Results.Conflict(new { message = "This storage target has backup history and cannot be deleted yet. Disable it instead." });

            var links = await db.DatabaseStorageTargets.Where(x => x.StorageTargetId == id).ToListAsync(ct);
            db.DatabaseStorageTargets.RemoveRange(links);
            db.StorageTargets.Remove(target);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapGet("/api/databases/{databaseId:guid}/storage-targets", async (
            Guid databaseId,
            OdinVaultDbContext db,
            CancellationToken ct) =>
        {
            if (!await db.DatabaseEndpoints.AnyAsync(x => x.Id == databaseId, ct))
                return Results.NotFound(new { message = "Database endpoint was not found." });

            var links = await db.DatabaseStorageTargets
                .Where(x => x.DatabaseEndpointId == databaseId)
                .ToListAsync(ct);
            var targetIds = links.Select(x => x.StorageTargetId).ToList();
            var targets = await db.StorageTargets.Where(x => targetIds.Contains(x.Id)).ToListAsync(ct);
            var result = targets.Select(target => new
            {
                target.Id,
                target.Name,
                target.Type,
                target.IsEnabled,
                linkEnabled = links.First(x => x.StorageTargetId == target.Id).IsEnabled
            });
            return Results.Ok(result);
        });

        app.MapPut("/api/databases/{databaseId:guid}/storage-targets/{targetId:guid}", async (
            Guid databaseId,
            Guid targetId,
            LinkStorageTargetRequest request,
            OdinVaultDbContext db,
            CancellationToken ct) =>
        {
            if (!await db.DatabaseEndpoints.AnyAsync(x => x.Id == databaseId, ct))
                return Results.NotFound(new { message = "Database endpoint was not found." });
            if (!await db.StorageTargets.AnyAsync(x => x.Id == targetId, ct))
                return Results.NotFound(new { message = "Storage target was not found." });

            var link = await db.DatabaseStorageTargets
                .FirstOrDefaultAsync(x => x.DatabaseEndpointId == databaseId && x.StorageTargetId == targetId, ct);
            if (link is null)
            {
                link = new DatabaseStorageTarget
                {
                    DatabaseEndpointId = databaseId,
                    StorageTargetId = targetId,
                    IsEnabled = request.IsEnabled
                };
                db.DatabaseStorageTargets.Add(link);
            }
            else
            {
                link.IsEnabled = request.IsEnabled;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { databaseId, targetId, link.IsEnabled });
        });

        app.MapDelete("/api/databases/{databaseId:guid}/storage-targets/{targetId:guid}", async (
            Guid databaseId,
            Guid targetId,
            OdinVaultDbContext db,
            CancellationToken ct) =>
        {
            var link = await db.DatabaseStorageTargets
                .FirstOrDefaultAsync(x => x.DatabaseEndpointId == databaseId && x.StorageTargetId == targetId, ct);
            if (link is null)
                return Results.NotFound();
            db.DatabaseStorageTargets.Remove(link);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapGet("/api/backups/{backupId:guid}/replicas", async (
            Guid backupId,
            OdinVaultDbContext db,
            CancellationToken ct) =>
        {
            if (!await db.BackupRecords.AnyAsync(x => x.Id == backupId, ct))
                return Results.NotFound(new { message = "Backup was not found." });

            var replicas = await db.BackupReplicas
                .Where(x => x.BackupRecordId == backupId)
                .OrderBy(x => x.StartedAtUtc)
                .ToListAsync(ct);
            return Results.Ok(replicas);
        });

        app.MapPost("/api/backups/{backupId:guid}/replicate", async (
            Guid backupId,
            OdinVaultDbContext db,
            StorageReplicationService replication,
            CancellationToken ct) =>
        {
            var backup = await db.BackupRecords.FirstOrDefaultAsync(x => x.Id == backupId, ct);
            if (backup is null)
                return Results.NotFound(new { message = "Backup was not found." });
            if (backup.Status != BackupStatus.Succeeded)
                return Results.Conflict(new { message = "Only successful backups can be replicated." });

            await replication.ReplicateAsync(backup, ct);
            var replicas = await db.BackupReplicas.Where(x => x.BackupRecordId == backupId).ToListAsync(ct);
            return Results.Ok(replicas);
        });
    }
}

public sealed record StartGoogleDrivePairingRequest(string TargetName, string RedirectUri, string? FolderId);
public sealed record CompleteGoogleDrivePairingRequest(string State, string Code, string? AccountEmail);
public sealed record UpdateStorageTargetRequest(string Name, string? FolderId, bool IsEnabled);
public sealed record LinkStorageTargetRequest(bool IsEnabled = true);
