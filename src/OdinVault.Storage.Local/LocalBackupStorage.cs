using OdinVault.Core;

namespace OdinVault.Storage.Local;

public sealed class LocalBackupStorage(string rootDirectory) : IBackupStorageProvider
{
    public StorageProviderType Type => StorageProviderType.Local;

    public async Task<StorageUploadResult> UploadAsync(
        StorageUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(rootDirectory);
        var destinationName = string.IsNullOrWhiteSpace(request.DestinationPath)
            ? request.FileName
            : Path.GetFileName(request.DestinationPath);
        var destinationPath = Resolve(destinationName);

        await using var source = new FileStream(
            request.LocalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await source.CopyToAsync(destination, 1024 * 1024, cancellationToken);
        await destination.FlushAsync(cancellationToken);

        return new StorageUploadResult(
            "local",
            destinationName,
            destinationPath,
            new FileInfo(destinationPath).Length);
    }

    public Task<Stream> OpenReadAsync(string remoteId, CancellationToken cancellationToken = default)
    {
        Stream stream = new FileStream(
            Resolve(remoteId),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string remoteId, CancellationToken cancellationToken = default)
    {
        var path = Resolve(remoteId);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string name)
    {
        var safeName = Path.GetFileName(name);
        if (!string.Equals(name, safeName, StringComparison.Ordinal))
            throw new ArgumentException("Invalid storage file name.", nameof(name));
        return Path.Combine(rootDirectory, safeName);
    }
}
