namespace OdinVault.Core;

public enum DatabaseEngine
{
    SqlServer = 1
}

public enum BackupStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3
}

public enum VerificationStatus
{
    NotRequested = 0,
    Pending = 1,
    Verified = 2,
    Failed = 3
}

public sealed class DatabaseEndpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DatabaseEngine Engine { get; set; } = DatabaseEngine.SqlServer;
    public string Host { get; set; } = string.Empty;
    public int? Port { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? ProtectedPassword { get; set; }
    public bool TrustServerCertificate { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BackupPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DatabaseEndpointId { get; set; }
    public string BackupDirectory { get; set; } = string.Empty;
    public string? ScheduleCron { get; set; }
    public int MaxLocalBackups { get; set; } = 7;
    public bool VerifyAfterBackup { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
}

public sealed class BackupRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DatabaseEndpointId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public BackupStatus Status { get; set; } = BackupStatus.Pending;
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.NotRequested;
    public long? SizeBytes { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? Error { get; set; }
}

public sealed record DatabaseConnectionInfo(
    string Host,
    int? Port,
    string DatabaseName,
    string Username,
    string? Password,
    bool TrustServerCertificate);

public sealed record BackupExecutionRequest(
    Guid DatabaseEndpointId,
    DatabaseConnectionInfo Connection,
    string BackupDirectory,
    bool VerifyAfterBackup);

public sealed record BackupExecutionResult(
    string FileName,
    string FilePath,
    long SizeBytes,
    VerificationStatus VerificationStatus);
