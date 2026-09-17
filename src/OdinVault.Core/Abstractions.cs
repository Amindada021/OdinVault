namespace OdinVault.Core;

public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}

public interface IDatabaseBackupProvider
{
    DatabaseEngine Engine { get; }
    Task TestConnectionAsync(DatabaseConnectionInfo connection, CancellationToken cancellationToken = default);
    Task<BackupExecutionResult> CreateBackupAsync(BackupExecutionRequest request, CancellationToken cancellationToken = default);
}

public interface IBackupStorage
{
    string Name { get; }
    Task StoreAsync(string sourceFilePath, string destinationName, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string name, CancellationToken cancellationToken = default);
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
}
