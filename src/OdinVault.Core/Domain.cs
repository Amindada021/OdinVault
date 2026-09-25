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

public enum StorageProviderType
{
    Local = 1,
    GoogleDrive = 2,
    S3 = 3,
    Sftp = 4,
    OdinVaultReplica = 5
}

public enum ReplicaStatus
{
    Pending = 0,
    Uploading = 1,
    Succeeded = 2,
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
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
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
    public DateTime? LastScheduledRunUtc { get; set; }
}

public sealed class BackupRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DatabaseEndpointId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public bool LocalFileAvailable { get; set; }
    public DateTime? LocalFileDeletedAtUtc { get; set; }
    public BackupStatus Status { get; set; } = BackupStatus.Pending;
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.NotRequested;
    public long? SizeBytes { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public string? Error { get; set; }
}

public sealed class StorageTarget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public StorageProviderType Type { get; set; }
    public bool IsEnabled { get; set; } = true;

    public string? FolderId { get; set; }
    public string? AccountEmail { get; set; }
    public string? ProtectedRefreshToken { get; set; }

    public string? BaseUrl { get; set; }
    public string? ProtectedApiKey { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class DatabaseStorageTarget
{
    public Guid DatabaseEndpointId { get; set; }
    public Guid StorageTargetId { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class BackupReplica
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BackupRecordId { get; set; }
    public Guid StorageTargetId { get; set; }
    public ReplicaStatus Status { get; set; } = ReplicaStatus.Pending;
    public string? RemoteId { get; set; }
    public string? RemotePath { get; set; }
    public long? SizeBytes { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }
    public DateTime? NextRetryAtUtc { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
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
    bool VerifyAfterBackup,
    IProgress<BackupProgress>? Progress = null);

public sealed record BackupExecutionResult(
    string FileName,
    string FilePath,
    long SizeBytes,
    VerificationStatus VerificationStatus);

public sealed record StorageUploadRequest(
    Guid BackupRecordId,
    string FileName,
    string LocalPath,
    string? DestinationPath = null,
    string? DatabaseName = null,
    string? SourceName = null);

public sealed record StorageUploadResult(
    string Provider,
    string RemoteId,
    string? RemotePath,
    long SizeBytes);

public interface IBackupStorageProvider
{
    StorageProviderType Type { get; }
    Task<StorageUploadResult> UploadAsync(StorageUploadRequest request, CancellationToken cancellationToken = default);
    Task DownloadToAsync(string remoteId, Stream destination, CancellationToken cancellationToken = default);
    Task DeleteAsync(string remoteId, CancellationToken cancellationToken = default);
}

public sealed record BackupProgress(string Stage, int? Percent = null, BackupRecord? Backup = null);
