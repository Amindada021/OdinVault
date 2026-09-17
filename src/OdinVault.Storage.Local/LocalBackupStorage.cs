using OdinVault.Core;

namespace OdinVault.Storage.Local;

public sealed class LocalBackupStorage(string rootDirectory) : IBackupStorage
{
    public string Name => "local";

    public async Task StoreAsync(string sourceFilePath, string destinationName, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(rootDirectory);
        var destinationPath = Resolve(destinationName);

        await using var source = new FileStream(
            sourceFilePath,
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
    }

    public Task<Stream> OpenReadAsync(string name, CancellationToken cancellationToken = default)
    {
        Stream stream = new FileStream(
            Resolve(name),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = Resolve(name);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(rootDirectory);
        IReadOnlyList<string> files = Directory
            .EnumerateFiles(rootDirectory, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult(files);
    }

    private string Resolve(string name)
    {
        var safeName = Path.GetFileName(name);
        if (!string.Equals(name, safeName, StringComparison.Ordinal))
            throw new ArgumentException("Invalid storage file name.", nameof(name));
        return Path.Combine(rootDirectory, safeName);
    }
}
