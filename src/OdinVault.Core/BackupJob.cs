namespace OdinVault.Core;

public enum BackupJobStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Interrupted = 4
}

public sealed class BackupJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Guid DatabaseEndpointId { get; set; }
    public BackupJobStatus Status { get; set; } = BackupJobStatus.Queued;
    public string Stage { get; set; } = "queued";
    public int? Percent { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? ExecutionToken { get; set; }
    public Guid? BackupRecordId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
