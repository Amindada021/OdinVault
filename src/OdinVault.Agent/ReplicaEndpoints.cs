namespace OdinVault.Agent;

public static class ReplicaEndpoints
{
    public static void MapReplicaEndpoints(this WebApplication app, string replicaDirectory)
    {
        Directory.CreateDirectory(replicaDirectory);

        app.MapPost("/api/replica/backups", async (HttpRequest request, CancellationToken ct) =>
        {
            var backupId = request.Headers["X-OdinVault-Backup-Id"].ToString();
            var fileName = Path.GetFileName(request.Headers["X-OdinVault-File-Name"].ToString());
            var expectedSizeText = request.Headers["X-OdinVault-File-Size"].ToString();

            if (string.IsNullOrWhiteSpace(backupId) || string.IsNullOrWhiteSpace(fileName))
                return Results.BadRequest(new { message = "Replica metadata headers are required." });

            if (!long.TryParse(expectedSizeText, out var expectedSize) || expectedSize < 1)
                return Results.BadRequest(new { message = "Invalid replica file size." });

            var safeId = Guid.TryParse(backupId, out var parsedId) ? parsedId.ToString("N") : Guid.NewGuid().ToString("N");
            var finalName = $"{safeId}_{fileName}";
            var finalPath = Path.Combine(replicaDirectory, finalName);
            var partPath = finalPath + ".part";

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
            var safeId = Path.GetFileName(id);
            if (!string.Equals(id, safeId, StringComparison.Ordinal)) return Results.BadRequest();
            var path = Path.Combine(replicaDirectory, safeId);
            return File.Exists(path)
                ? Results.File(path, "application/octet-stream", enableRangeProcessing: true)
                : Results.NotFound();
        });

        app.MapDelete("/api/replica/backups/{id}", (string id) =>
        {
            var safeId = Path.GetFileName(id);
            if (!string.Equals(id, safeId, StringComparison.Ordinal)) return Results.BadRequest();
            var path = Path.Combine(replicaDirectory, safeId);
            if (File.Exists(path)) File.Delete(path);
            return Results.NoContent();
        });
    }
}
