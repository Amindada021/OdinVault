using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using OdinVault.Core;

namespace OdinVault.Agent;

public static class ReplicaEndpoints
{
    public static void MapReplicaEndpoints(this WebApplication app, ReplicaSettings settings)
    {
        app.MapGet("/api/replica/settings", () => Results.Ok(new { directory = settings.Directory }));
        app.MapPut("/api/replica/settings", (ReplicaDirectoryRequest request) =>
        {
            try { settings.Save(request.Directory); return Results.Ok(new { directory = settings.Directory }); }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            { return Results.BadRequest(new { message = ex.Message }); }
        });
        app.MapGet("/api/replica/received", () =>
        {
            var root = settings.Directory;
            var files = Directory.EnumerateFiles(root, "*.bak", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTimeUtc).Take(200)
                .Select(x => new { fileName = x.Name, relativePath = Path.GetRelativePath(root, x.FullName), sizeBytes = x.Length, receivedAtUtc = x.LastWriteTimeUtc });
            return Results.Ok(files.ToArray());
        });

        app.MapPost("/api/replica/backups", async (HttpRequest request, CancellationToken ct) =>
        {
            var limit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = null;
            var replicaDirectory = settings.Directory;
            var backupId = request.Headers["X-OdinVault-Backup-Id"].ToString();
            var encoded = request.Headers["X-OdinVault-Name-Encoding"] == "uri";
            string Header(string name) => encoded ? Uri.UnescapeDataString(request.Headers[name].ToString()) : request.Headers[name].ToString();
            var fileName = Path.GetFileName(Header("X-OdinVault-File-Name"));
            var expectedSizeText = request.Headers["X-OdinVault-File-Size"].ToString();
            var expectedHash = request.Headers["X-OdinVault-SHA256"].ToString().Trim();

            if (string.IsNullOrWhiteSpace(backupId) || string.IsNullOrWhiteSpace(fileName))
                return Results.BadRequest(new { message = "Replica metadata headers are required." });

            if (!long.TryParse(expectedSizeText, out var expectedSize) || expectedSize < 1)
                return Results.BadRequest(new { message = "Invalid replica file size." });

            var safeId = Guid.TryParse(backupId, out var parsedId) ? parsedId.ToString("N") : Guid.NewGuid().ToString("N");
            var relative = encoded
                ? Path.Combine(BackupNaming.SafeSegment(Header("X-OdinVault-Source")), BackupNaming.SafeSegment(Header("X-OdinVault-Database")), fileName)
                : $"{safeId}_{fileName}";
            var finalName = encoded ? "v2_" + Convert.ToBase64String(Encoding.UTF8.GetBytes(relative)).TrimEnd('=').Replace('+', '-').Replace('/', '_') : relative;
            var finalPath = Path.Combine(replicaDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            var partPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".part";

            if (!string.IsNullOrEmpty(expectedHash) &&
                (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit)))
            {
                return Results.BadRequest(new { message = "Invalid replica SHA256." });
            }

            try
            {
                long actualSize = 0;
                string actualHash;
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
                try
                {
                    await using var output = new FileStream(
                        partPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);

                    while (true)
                    {
                        var read = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                        if (read == 0)
                            break;

                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        hasher.AppendData(buffer, 0, read);
                        actualSize += read;
                    }

                    await output.FlushAsync(ct);
                    actualHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                if (actualSize != expectedSize)
                    throw new IOException($"Replica size mismatch. Expected {expectedSize}, received {actualSize}.");

                if (!string.IsNullOrEmpty(expectedHash) &&
                    !string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Replica SHA256 mismatch.");
                }

                File.Move(partPath, finalPath, true);
                return Results.Ok(new
                {
                    id = finalName,
                    path = finalPath,
                    sizeBytes = actualSize,
                    sha256 = actualHash
                });
            }
            catch
            {
                if (File.Exists(partPath)) File.Delete(partPath);
                throw;
            }
        });

        app.MapGet("/api/replica/backups/{id}", (string id) =>
        {
            var path = ResolveReplicaPath(settings.Directory, id);
            if (path is null) return Results.BadRequest();
            return File.Exists(path)
                ? Results.File(path, "application/octet-stream", enableRangeProcessing: true)
                : Results.NotFound();
        });

        app.MapDelete("/api/replica/backups/{id}", (string id) =>
        {
            var path = ResolveReplicaPath(settings.Directory, id);
            if (path is null) return Results.BadRequest();
            if (File.Exists(path)) File.Delete(path);
            return Results.NoContent();
        });
    }
    private static string? ResolveReplicaPath(string root, string id)
    {
        try
        {
            var relative = id;
            if (id.StartsWith("v2_", StringComparison.Ordinal))
            {
                var payload = id[3..].Replace('-', '+').Replace('_', '/');
                relative = Encoding.UTF8.GetString(Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '=')));
            }
            else if (id != Path.GetFileName(id)) return null;
            if (Path.IsPathRooted(relative) || relative.Split('/', '\\').Any(x => x is ".." or ".")) return null;
            var full = Path.GetFullPath(Path.Combine(root, relative));
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ? full : null;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or IOException) { return null; }
    }

}
