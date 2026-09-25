namespace OdinVault.Core;

public enum BackupJobEnqueueStatus
{
    Created,
    Existing,
    ActiveConflict,
    RequestConflict
}

public enum BackupJobMutationStatus
{
    Updated,
    NotFound,
    StateConflict,
    OwnershipConflict,
    ValidationConflict
}

public sealed record BackupJobSnapshot(
    Guid Id,
    Guid RequestId,
    Guid DatabaseEndpointId,
    BackupJobStatus Status,
    string Stage,
    int? Percent,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    Guid? ExecutionToken,
    Guid? BackupRecordId,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record BackupJobEnqueueResult(
    BackupJobEnqueueStatus Status,
    BackupJobSnapshot? Job,
    Guid? ConflictingJobId = null);

public sealed record BackupJobClaimResult(
    BackupJobMutationStatus Status,
    BackupJobSnapshot? Job);

public sealed record BackupJobMutationResult(
    BackupJobMutationStatus Status,
    BackupJobSnapshot? Job);

public interface IBackupJobStore
{
    Task<BackupJobEnqueueResult> EnqueueAsync(
        Guid requestId,
        Guid databaseEndpointId,
        CancellationToken cancellationToken = default);

    Task<BackupJobSnapshot?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<BackupJobSnapshot?> GetActiveForDatabaseAsync(
        Guid databaseEndpointId,
        CancellationToken cancellationToken = default);

    Task<BackupJobClaimResult> TryClaimNextAsync(
        CancellationToken cancellationToken = default);

    Task<BackupJobMutationResult> UpdateProgressAsync(
        Guid id,
        Guid executionToken,
        string stage,
        int? percent,
        CancellationToken cancellationToken = default);

    Task<BackupJobMutationResult> FailQueuedAsync(
        Guid id,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken = default);

    Task<BackupJobMutationResult> CompleteAsync(
        Guid id,
        Guid executionToken,
        Guid backupRecordId,
        CancellationToken cancellationToken = default);

    Task<BackupJobMutationResult> FailAsync(
        Guid id,
        Guid executionToken,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken = default);

    Task<BackupJobMutationResult> InterruptAsync(
        Guid id,
        Guid executionToken,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken = default);
}
