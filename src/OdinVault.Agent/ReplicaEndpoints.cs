using System.Text;
using OdinVault.Core;

namespace OdinVault.Agent;

public static class ReplicaEndpoints
{
    public static void MapReplicaEndpoints(this WebApplication app, string replicaDirectory)
    {
        Directory.CreateDirectory(replicaDirectory);

        app.MapPost("/api/replica/backups", async (HttpRequest request, CancellationToken ct) =>
        {
            var backupId = request.Headers["X-OdinVault-Backup-Id"].ToString();
            var encoded = request.Headers["X-OdinVault-Name-Encoding"] == "uri";
            string Header(string name) => encoded ? Uri.UnescapeDataString(request.Headers[name].ToString()) : request.Headers[name].ToString();
            var fileName = Path.GetFileName(Header("X-OdinVault-File-Name"));
            var expectedSizeText = request.Headers["X-OdinVault-File-Size"].ToString();

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

            try
            {
                await using (var output = new FileStream(
                    partPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await request.Body.CopyToAsync(output, 1024 * 1024, ct);
                    await output.FlushAsync(ct);
                }

                var actualSize = new FileInfo(partPath).Length;
                if (actualSize != expectedSize)
                    throw new IOException($"Replica size mismatch. Expected {expectedSize}, received {actualSize}.");

                File.Move(partPath, finalPath, true);
                return Results.Ok(new { id = finalName, path = finalPath, sizeBytes = actualSize });
            }
            catch
            {
                if (File.Exists(partPath)) File.Delete(partPath);
                throw;
            }
        });

        app.MapGet("/api/replica/backups/{id}", (string id) =>
        {
            var path = ResolveReplicaPath(replicaDirectory, id);
            if (path is null) return Results.BadRequest();
            return File.Exists(path)
                ? Results.File(path, "application/octet-stream", enableRangeProcessing: true)
                : Results.NotFound();
        });

        app.MapDelete("/api/replica/backups/{id}", (string id) =>
        {
            var path = ResolveReplicaPath(replicaDirectory, id);
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
