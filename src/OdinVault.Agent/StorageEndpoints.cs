using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;
using OdinVault.Storage.GoogleDrive;

namespace OdinVault.Agent;

public static class StorageEndpoints
{
    public static void MapStorageEndpoints(this WebApplication app)
    {
        app.MapGet("/api/storage/overview", async (
            OdinVaultDbContext db,
            AgentPaths paths,
            CancellationToken ct) =>
        {
            var targets = await db.StorageTargets
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .ToListAsync(ct);

            var targetIds = targets.Select(x => x.Id).ToList();
            var replicas = await db.BackupReplicas
                .AsNoTracking()
                .Where(x => targetIds.Contains(x.StorageTargetId))
                .OrderByDescending(x => x.StartedAtUtc)
                .ToListAsync(ct);

            var links = await db.DatabaseStorageTargets
                .AsNoTracking()
                .Where(x => targetIds.Contains(x.StorageTargetId) && x.IsEnabled)
                .ToListAsync(ct);

            var localFreeBytes = TryGetAvailableFreeSpace(paths.StorageDirectory);

            var items = targets.Select(target =>
            {
                var targetReplicas = replicas
                    .Where(x => x.StorageTargetId == target.Id)
                    .ToList();
                var latest = targetReplicas.FirstOrDefault();
                var latestSuccess = targetReplicas
                    .Where(x => x.Status == ReplicaStatus.Succeeded)
                    .OrderByDescending(x => x.CompletedAtUtc ?? x.StartedAtUtc)
                    .FirstOrDefault();
                var latestFailure = targetReplicas
                    .Where(x => x.Status == ReplicaStatus.Failed)
                    .OrderByDescending(x => x.CompletedAtUtc ?? x.StartedAtUtc)
                    .FirstOrDefault();

                return new
                {
                    target.Id,
                    target.Name,
                    type = (int)target.Type,
                    target.IsEnabled,
                    target.FolderId,
                    target.AccountEmail,
                    target.BaseUrl,
                    isConnected = target.Type == StorageProviderType.GoogleDrive
                        ? target.ProtectedRefreshToken != null
                        : target.Type == StorageProviderType.OdinVaultReplica
                            ? target.ProtectedApiKey != null
                            : true,
                    linkedDatabases = links.Count(x => x.StorageTargetId == target.Id),
                    succeededReplicas = targetReplicas.Count(x => x.Status == ReplicaStatus.Succeeded),
                    failedReplicas = targetReplicas.Count(x => x.Status == ReplicaStatus.Failed),
                    lastActivityAtUtc = latest?.CompletedAtUtc ?? latest?.StartedAtUtc,
                    lastSuccessAtUtc = latestSuccess?.CompletedAtUtc ?? latestSuccess?.StartedAtUtc,
                    lastFailureAtUtc = latestFailure?.CompletedAtUtc ?? latestFailure?.StartedAtUtc,
                    lastError = latestFailure?.Error
                };
            }).ToArray();

            return Results.Ok(new
            {
                local = new
                {
                    name = "Local Storage",
                    directory = paths.StorageDirectory,
                    freeBytes = localFreeBytes,
                    exists = Directory.Exists(paths.StorageDirectory),
                    writable = CanWriteDirectory(paths.StorageDirectory)
                },
                targets = items
            });
        });

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
                    x.BaseUrl,
                    isConnected = x.Type == StorageProviderType.GoogleDrive
                        ? x.ProtectedRefreshToken != null
                        : x.Type == StorageProviderType.OdinVaultReplica
                            ? x.ProtectedApiKey != null
                            : true,
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

        app.MapGet("/api/storage-targets/google-drive/pair/status/{state}", (
            string state,
            GoogleDrivePairingStateStore stateStore) =>
        {
            var status = stateStore.GetStatus(state);
            return status is null
                ? Results.NotFound(new { message = "OAuth pairing state was not found or expired." })
                : Results.Ok(status);
        });

        app.MapGet("/api/storage-targets/google-drive/callback", async (
            string? code,
            string? state,
            string? error,
            GoogleDrivePairingStateStore stateStore,
            GoogleDriveOAuthService oauth,
            ISecretProtector protector,
            OdinVaultDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(state))
                return Results.Text("OdinVault Google Drive pairing failed: missing state.", "text/plain");

            if (!string.IsNullOrWhiteSpace(error))
            {
                stateStore.MarkFailed(state, error);
                return Results.Text($"OdinVault Google Drive pairing failed: {error}. You can return to the app.", "text/plain");
            }

            if (string.IsNullOrWhiteSpace(code) || !stateStore.TryConsume(state, out var pairing))
            {
                stateStore.MarkFailed(state, "OAuth state is invalid, expired, or authorization code is missing.");
                return Results.Text("OdinVault Google Drive pairing failed. The request is invalid or expired.", "text/plain");
            }

            try
            {
                var token = await oauth.ExchangeCodeAsync(code, pairing.RedirectUri, ct);
                if (string.IsNullOrWhiteSpace(token.RefreshToken))
                    throw new InvalidOperationException("Google did not return a refresh token. Revoke OdinVault access and pair again.");

                var target = new StorageTarget
                {
                    Name = pairing.TargetName,
                    Type = StorageProviderType.GoogleDrive,
                    FolderId = pairing.FolderId,
                    ProtectedRefreshToken = protector.Protect(token.RefreshToken),
                    IsEnabled = true
                };

                db.StorageTargets.Add(target);
                await db.SaveChangesAsync(ct);
                stateStore.MarkSucceeded(state, target.Id);
                return Results.Text("Google Drive connected successfully to OdinVault. You can return to the app.", "text/plain");
            }
            catch (Exception ex)
            {
                stateStore.MarkFailed(state, ex.Message);
                return Results.Text($"OdinVault Google Drive pairing failed: {ex.Message}", "text/plain");
            }
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
            if (string.IsNullOrWhiteSpace(token.RefreshToken))
                return Results.BadRequest(new { message = "Google did not return a refresh token." });

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
            stateStore.MarkSucceeded(request.State, target.Id);
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
            ISecretProtector protector,
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

            if (target.Type == StorageProviderType.OdinVaultReplica)
            {
                if (!string.IsNullOrWhiteSpace(request.BaseUrl))
                {
                    if (!Uri.TryCreate(request.BaseUrl.Trim(), UriKind.Absolute, out var uri) ||
                        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    {
                        return Results.BadRequest(new { message = "baseUrl must be an absolute HTTP or HTTPS URL." });
                    }

                    target.BaseUrl = request.BaseUrl.Trim().TrimEnd('/');
                }

                if (!string.IsNullOrWhiteSpace(request.ApiKey))
                    target.ProtectedApiKey = protector.Protect(request.ApiKey.Trim());
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new
            {
                target.Id,
                target.Name,
                target.Type,
                target.FolderId,
                target.AccountEmail,
                target.BaseUrl,
                target.IsEnabled
            });
        });

        app.MapPost("/api/storage-targets/{id:guid}/connection-test", async (
            Guid id,
            OdinVaultDbContext db,
            ISecretProtector protector,
            GoogleDriveOAuthService oauth,
            IHttpClientFactory httpClientFactory,
            CancellationToken ct) =>
        {
            var target = await db.StorageTargets
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (target is null)
                return Results.NotFound(new { message = "Storage target was not found." });

            try
            {
                switch (target.Type)
                {
                    case StorageProviderType.GoogleDrive:
                    {
                        if (string.IsNullOrWhiteSpace(target.ProtectedRefreshToken))
                            return Results.BadRequest(new { message = "Google Drive target is not connected." });

                        var refreshToken = protector.Unprotect(target.ProtectedRefreshToken);
                        using var drive = oauth.CreateDriveService(refreshToken);
                        var request = drive.Files.List();
                        request.PageSize = 1;
                        request.Fields = "files(id)";
                        await request.ExecuteAsync(ct);
                        return Results.Ok(new { success = true, message = "Google Drive connection succeeded." });
                    }

                    case StorageProviderType.OdinVaultReplica:
                    {
                        if (string.IsNullOrWhiteSpace(target.BaseUrl) ||
                            string.IsNullOrWhiteSpace(target.ProtectedApiKey))
                        {
                            return Results.BadRequest(new { message = "Replica target is incomplete." });
                        }

                        var apiKey = protector.Unprotect(target.ProtectedApiKey);
                        var client = httpClientFactory.CreateClient("OdinVaultReplica");
                        using var request = new HttpRequestMessage(
                            HttpMethod.Get,
                            $"{target.BaseUrl.TrimEnd('/')}/api/health");
                        request.Headers.TryAddWithoutValidation("X-OdinVault-Key", apiKey);
                        using var response = await client.SendAsync(request, ct);
                        return Results.Ok(new
                        {
                            success = response.IsSuccessStatusCode,
                            message = response.IsSuccessStatusCode
                                ? "OdinVault Replica connection succeeded."
                                : $"Replica returned HTTP {(int)response.StatusCode}."
                        });
                    }

                    default:
                        return Results.BadRequest(new { message = "This storage target does not support connection testing." });
                }
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = ex.Message });
            }
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
    private static long? TryGetAvailableFreeSpace(string directory)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            return string.IsNullOrWhiteSpace(root)
                ? null
                : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool CanWriteDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".odinvault-storage-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            return false;
        }
    }
}


public sealed record StartGoogleDrivePairingRequest(string TargetName, string RedirectUri, string? FolderId);
public sealed record CompleteGoogleDrivePairingRequest(string State, string Code, string? AccountEmail);
public sealed record UpdateStorageTargetRequest(
    string Name,
    string? FolderId,
    bool IsEnabled,
    string? BaseUrl = null,
    string? ApiKey = null);
public sealed record LinkStorageTargetRequest(bool IsEnabled = true);
