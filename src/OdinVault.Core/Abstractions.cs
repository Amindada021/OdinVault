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
