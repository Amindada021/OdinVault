using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using OdinVault.Core;

namespace OdinVault.Agent;

public sealed class OdinVaultReplicaStorage(
    HttpClient httpClient,
    string baseUrl,
    string apiKey) : IBackupStorageProvider
{
    public StorageProviderType Type => StorageProviderType.OdinVaultReplica;

    public async Task<StorageUploadResult> UploadAsync(
        StorageUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(request.LocalPath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Backup file was not found.", request.LocalPath);

        string contentHash;
        await using (var hashStream = new FileStream(
            request.LocalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            contentHash = Convert.ToHexString(
                await SHA256.HashDataAsync(hashStream, cancellationToken))
                .ToLowerInvariant();
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/replica/backups");
        message.Headers.TryAddWithoutValidation("X-OdinVault-Key", apiKey);
        message.Headers.TryAddWithoutValidation("X-OdinVault-Backup-Id", request.BackupRecordId.ToString());
        message.Headers.TryAddWithoutValidation("X-OdinVault-File-Name", Uri.EscapeDataString(request.FileName));
        message.Headers.TryAddWithoutValidation("X-OdinVault-Name-Encoding", "uri");
        message.Headers.TryAddWithoutValidation("X-OdinVault-Database", Uri.EscapeDataString(request.DatabaseName ?? "database"));
        message.Headers.TryAddWithoutValidation("X-OdinVault-Source", Uri.EscapeDataString(request.SourceName ?? "server"));
        message.Headers.TryAddWithoutValidation("X-OdinVault-File-Size", fileInfo.Length.ToString());
        message.Headers.TryAddWithoutValidation("X-OdinVault-SHA256", contentHash);

        await using var stream = new FileStream(
            request.LocalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        message.Content = new StreamContent(stream, 1024 * 1024);
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        message.Content.Headers.ContentLength = fileInfo.Length;

        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Replica upload failed with {(int)response.StatusCode}: {body}");
        }

        var result = await response.Content.ReadFromJsonAsync<ReplicaUploadResponse>(cancellationToken: cancellationToken)
            ?? throw new IOException("Replica Agent returned an empty response.");

        if (!string.IsNullOrWhiteSpace(result.Sha256) &&
            !string.Equals(result.Sha256, contentHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Replica Agent returned a content hash that does not match the source backup.");
        }

        return new StorageUploadResult(
            "odinvault-replica",
            result.Id,
            result.Path,
            result.SizeBytes,
            string.IsNullOrWhiteSpace(result.Sha256) ? null : result.Sha256);
    }

    public async Task DownloadToAsync(string remoteId, Stream destination, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/api/replica/backups/{Uri.EscapeDataString(remoteId)}");
        request.Headers.TryAddWithoutValidation("X-OdinVault-Key", apiKey);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await source.CopyToAsync(destination, 1024 * 1024, cancellationToken);
    }

    public async Task DeleteAsync(string remoteId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{baseUrl.TrimEnd('/')}/api/replica/backups/{Uri.EscapeDataString(remoteId)}");
        request.Headers.TryAddWithoutValidation("X-OdinVault-Key", apiKey);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record ReplicaUploadResponse(string Id, string Path, long SizeBytes, string? Sha256);
}
