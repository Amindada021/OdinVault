using Microsoft.EntityFrameworkCore;
using OdinVault.Core;

namespace OdinVault.Persistence;

public sealed class SqliteBackupJobStore(OdinVaultDbContext db) : IBackupJobStore
{
    public async Task<BackupJobEnqueueResult> EnqueueAsync(
        Guid requestId,
        Guid databaseEndpointId,
        CancellationToken cancellationToken = default)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("RequestId cannot be empty.", nameof(requestId));
        if (databaseEndpointId == Guid.Empty) throw new ArgumentException("DatabaseEndpointId cannot be empty.", nameof(databaseEndpointId));

        DbUpdateException? lastUniqueConflict = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var existing = await db.BackupJobs.AsNoTracking()
                .FirstOrDefaultAsync(x => x.RequestId == requestId, cancellationToken);
            if (existing is not null)
            {
                return existing.DatabaseEndpointId == databaseEndpointId
                    ? new BackupJobEnqueueResult(BackupJobEnqueueStatus.Existing, Snapshot(existing))
                    : new BackupJobEnqueueResult(BackupJobEnqueueStatus.RequestConflict, Snapshot(existing), existing.Id);
            }

            var active = await db.BackupJobs.AsNoTracking()
                .Where(x => x.DatabaseEndpointId == databaseEndpointId &&
                            (x.Status == BackupJobStatus.Queued || x.Status == BackupJobStatus.Running))
                .OrderBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (active is not null)
            {
                return active.RequestId == requestId
                    ? new BackupJobEnqueueResult(BackupJobEnqueueStatus.Existing, Snapshot(active))
                    : new BackupJobEnqueueResult(BackupJobEnqueueStatus.ActiveConflict, Snapshot(active), active.Id);
            }

            var now = DateTime.UtcNow;
            var job = new BackupJob
            {
                RequestId = requestId,
                DatabaseEndpointId = databaseEndpointId,
                Status = BackupJobStatus.Queued,
                Stage = "queued",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            db.BackupJobs.Add(job);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return new BackupJobEnqueueResult(BackupJobEnqueueStatus.Created, Snapshot(job));
            }
            catch (DbUpdateException ex) when (IsQueueCapacityConflict(ex))
            {
                db.Entry(job).State = EntityState.Detached;
                return new BackupJobEnqueueResult(BackupJobEnqueueStatus.QueueFull, null);
            }
            catch (DbUpdateException ex) when (IsEnqueueUniqueConflict(ex))
            {
                db.Entry(job).State = EntityState.Detached;
                lastUniqueConflict = ex;
            }
        }

        var duplicate = await db.BackupJobs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RequestId == requestId, cancellationToken);
        if (duplicate is not null)
        {
            return duplicate.DatabaseEndpointId == databaseEndpointId
                ? new BackupJobEnqueueResult(BackupJobEnqueueStatus.Existing, Snapshot(duplicate))
                : new BackupJobEnqueueResult(BackupJobEnqueueStatus.RequestConflict, Snapshot(duplicate), duplicate.Id);
        }

        var conflicting = await db.BackupJobs.AsNoTracking()
            .Where(x => x.DatabaseEndpointId == databaseEndpointId &&
                        (x.Status == BackupJobStatus.Queued || x.Status == BackupJobStatus.Running))
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (conflicting is not null)
        {
            return conflicting.RequestId == requestId
                ? new BackupJobEnqueueResult(BackupJobEnqueueStatus.Existing, Snapshot(conflicting))
                : new BackupJobEnqueueResult(BackupJobEnqueueStatus.ActiveConflict, Snapshot(conflicting), conflicting.Id);
        }

        if (lastUniqueConflict is not null)
            throw lastUniqueConflict;

        throw new InvalidOperationException("Could not resolve backup job enqueue conflict.");
    }

    public async Task<BackupJobSnapshot?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var job = await db.BackupJobs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return job is null ? null : Snapshot(job);
    }

    public async Task<BackupJobSnapshot?> GetActiveForDatabaseAsync(
        Guid databaseEndpointId,
        CancellationToken cancellationToken = default)
    {
        var job = await db.BackupJobs.AsNoTracking()
            .Where(x => x.DatabaseEndpointId == databaseEndpointId &&
                        (x.Status == BackupJobStatus.Queued || x.Status == BackupJobStatus.Running))
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return job is null ? null : Snapshot(job);
    }

    public async Task<BackupJobClaimResult> TryClaimNextAsync(
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var candidateId = await db.BackupJobs.AsNoTracking()
                .Where(x => x.Status == BackupJobStatus.Queued)
                .OrderBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (candidateId is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new BackupJobClaimResult(BackupJobMutationStatus.NotFound, null);
            }

            var token = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var changed = await db.BackupJobs
                .Where(x => x.Id == candidateId.Value && x.Status == BackupJobStatus.Queued)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, BackupJobStatus.Running)
                    .SetProperty(x => x.Stage, "backup")
                    .SetProperty(x => x.ExecutionToken, token)
                    .SetProperty(x => x.StartedAtUtc, now)
                    .SetProperty(x => x.UpdatedAtUtc, now),
                    cancellationToken);

            if (changed == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            var claimed = await db.BackupJobs.AsNoTracking()
                .SingleAsync(x => x.Id == candidateId.Value, cancellationToken);
            var snapshot = Snapshot(claimed);

            await transaction.CommitAsync(cancellationToken);
            return new BackupJobClaimResult(BackupJobMutationStatus.Updated, snapshot);
        }

        return new BackupJobClaimResult(BackupJobMutationStatus.StateConflict, null);
    }

    public async Task<BackupJobMutationResult> UpdateProgressAsync(
        Guid id,
        Guid executionToken,
        string stage,
        int? percent,
        CancellationToken cancellationToken = default)
    {
        stage = ValidateStage(stage);
        if (percent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percent), "Percent must be null or between 0 and 100.");

        var existing = await db.BackupJobs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        var validation = ValidateRunningOwner(existing, executionToken);
        if (validation is not null) return validation;

        var now = DateTime.UtcNow;
        var changed = await db.BackupJobs
            .Where(x => x.Id == id &&
                        x.Status == BackupJobStatus.Running &&
                        x.ExecutionToken == executionToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Stage, stage)
                .SetProperty(x => x.Percent, percent)
                .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);

        return changed == 1
            ? await UpdatedAsync(id, cancellationToken)
            : await CurrentConflictAsync(id, executionToken, cancellationToken);
    }

    public async Task<BackupJobMutationResult> AttachBackupRecordAsync(
        Guid id,
        Guid executionToken,
        Guid backupRecordId,
        string stage,
        int? percent,
        CancellationToken cancellationToken = default)
    {
        stage = ValidateStage(stage);
        if (percent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percent), "Percent must be null or between 0 and 100.");

        var existing = await db.BackupJobs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        var validation = ValidateRunningOwner(existing, executionToken);
        if (validation is not null) return validation;

        var now = DateTime.UtcNow;
        var changed = await db.BackupJobs
            .Where(x => x.Id == id &&
                        x.Status == BackupJobStatus.Running &&
                        x.ExecutionToken == executionToken &&
                        db.BackupRecords.Any(backup =>
                            backup.Id == backupRecordId &&
                            (backup.Status == BackupStatus.Running || backup.Status == BackupStatus.Succeeded) &&
                            backup.DatabaseEndpointId == x.DatabaseEndpointId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Stage, stage)
                .SetProperty(x => x.Percent, percent)
                .SetProperty(x => x.BackupRecordId, backupRecordId)
                .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);

        if (changed == 1)
            return await UpdatedAsync(id, cancellationToken);

        var current = await GetEntityAsync(id, cancellationToken);
        if (current is null)
            return new BackupJobMutationResult(BackupJobMutationStatus.NotFound, null);
        if (current.Status != BackupJobStatus.Running)
            return new BackupJobMutationResult(BackupJobMutationStatus.StateConflict, Snapshot(current));
        if (current.ExecutionToken != executionToken)
            return new BackupJobMutationResult(BackupJobMutationStatus.OwnershipConflict, Snapshot(current));

        return new BackupJobMutationResult(BackupJobMutationStatus.ValidationConflict, Snapshot(current));
    }

    public async Task<BackupJobMutationResult> FailQueuedAsync(
        Guid id,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        (errorCode, errorMessage) = ValidateError(errorCode, errorMessage);
        var now = DateTime.UtcNow;
        var changed = await db.BackupJobs
            .Where(x => x.Id == id && x.Status == BackupJobStatus.Queued)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, BackupJobStatus.Failed)
                .SetProperty(x => x.Stage, "failed")
                .SetProperty(x => x.ErrorCode, errorCode)
                .SetProperty(x => x.ErrorMessage, errorMessage)
                .SetProperty(x => x.CompletedAtUtc, now)
                .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);

        if (changed == 1) return await UpdatedAsync(id, cancellationToken);
        var current = await GetEntityAsync(id, cancellationToken);
        return current is null
            ? new BackupJobMutationResult(BackupJobMutationStatus.NotFound, null)
            : new BackupJobMutationResult(BackupJobMutationStatus.StateConflict, Snapshot(current));
    }

    public async Task<BackupJobMutationResult> CompleteAsync(
        Guid id,
        Guid executionToken,
        Guid backupRecordId,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.BackupJobs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        var validation = ValidateRunningOwner(existing, executionToken);
        if (validation is not null) return validation;

        var now = DateTime.UtcNow;
        var changed = await db.BackupJobs
            .Where(x => x.Id == id &&
                        x.Status == BackupJobStatus.Running &&
                        x.ExecutionToken == executionToken &&
                        db.BackupRecords.Any(backup =>
                            backup.Id == backupRecordId &&
                            backup.Status == BackupStatus.Succeeded &&
                            backup.DatabaseEndpointId == x.DatabaseEndpointId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, BackupJobStatus.Succeeded)
                .SetProperty(x => x.Stage, "complete")
                .SetProperty(x => x.Percent, 100)
                .SetProperty(x => x.BackupRecordId, backupRecordId)
                .SetProperty(x => x.ErrorCode, (string?)null)
                .SetProperty(x => x.ErrorMessage, (string?)null)
                .SetProperty(x => x.CompletedAtUtc, now)
                .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);

        if (changed == 1)
            return await UpdatedAsync(id, cancellationToken);

        var current = await GetEntityAsync(id, cancellationToken);
        if (current is null)
            return new BackupJobMutationResult(BackupJobMutationStatus.NotFound, null);
        if (current.Status != BackupJobStatus.Running)
            return new BackupJobMutationResult(BackupJobMutationStatus.StateConflict, Snapshot(current));
        if (current.ExecutionToken != executionToken)
            return new BackupJobMutationResult(BackupJobMutationStatus.OwnershipConflict, Snapshot(current));

        return new BackupJobMutationResult(BackupJobMutationStatus.ValidationConflict, Snapshot(current));
    }

    public Task<BackupJobMutationResult> FailAsync(
        Guid id,
        Guid executionToken,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken = default) =>
        FinishRunningAsync(id, executionToken, BackupJobStatus.Failed, "failed", errorCode, errorMessage, cancellationToken);

    public Task<BackupJobMutationResult> InterruptAsync(
        Guid id,
        Guid executionToken,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken = default) =>
        FinishRunningAsync(id, executionToken, BackupJobStatus.Interrupted, "interrupted", errorCode, errorMessage, cancellationToken);

    private async Task<BackupJobMutationResult> FinishRunningAsync(
        Guid id,
        Guid executionToken,
        BackupJobStatus target,
        string stage,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        (errorCode, errorMessage) = ValidateError(errorCode, errorMessage);
        var existing = await db.BackupJobs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        var validation = ValidateRunningOwner(existing, executionToken);
        if (validation is not null) return validation;

        var now = DateTime.UtcNow;
        var changed = await db.BackupJobs
            .Where(x => x.Id == id &&
                        x.Status == BackupJobStatus.Running &&
                        x.ExecutionToken == executionToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, target)
                .SetProperty(x => x.Stage, stage)
                .SetProperty(x => x.ErrorCode, errorCode)
                .SetProperty(x => x.ErrorMessage, errorMessage)
                .SetProperty(x => x.CompletedAtUtc, now)
                .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);

        return changed == 1
            ? await UpdatedAsync(id, cancellationToken)
            : await CurrentConflictAsync(id, executionToken, cancellationToken);
    }

    private async Task<BackupJobMutationResult> CurrentConflictAsync(
        Guid id,
        Guid executionToken,
        CancellationToken cancellationToken)
    {
        var current = await GetEntityAsync(id, cancellationToken);
        if (current is null) return new BackupJobMutationResult(BackupJobMutationStatus.NotFound, null);
        return new BackupJobMutationResult(
            current.Status != BackupJobStatus.Running
                ? BackupJobMutationStatus.StateConflict
                : current.ExecutionToken != executionToken
                    ? BackupJobMutationStatus.OwnershipConflict
                    : BackupJobMutationStatus.StateConflict,
            Snapshot(current));
    }

    private async Task<BackupJobMutationResult> UpdatedAsync(Guid id, CancellationToken cancellationToken)
    {
        var updated = await db.BackupJobs.AsNoTracking()
            .SingleAsync(x => x.Id == id, cancellationToken);
        return new BackupJobMutationResult(BackupJobMutationStatus.Updated, Snapshot(updated));
    }

    private Task<BackupJob?> GetEntityAsync(Guid id, CancellationToken cancellationToken) =>
        db.BackupJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    private static BackupJobMutationResult? ValidateRunningOwner(BackupJob? job, Guid executionToken)
    {
        if (job is null) return new BackupJobMutationResult(BackupJobMutationStatus.NotFound, null);
        if (job.Status != BackupJobStatus.Running)
            return new BackupJobMutationResult(BackupJobMutationStatus.StateConflict, Snapshot(job));
        if (job.ExecutionToken != executionToken)
            return new BackupJobMutationResult(BackupJobMutationStatus.OwnershipConflict, Snapshot(job));
        return null;
    }

    private static bool IsQueueCapacityConflict(DbUpdateException exception)
    {
        return exception.InnerException is Microsoft.Data.Sqlite.SqliteException sqlite &&
               sqlite.SqliteErrorCode == 19 &&
               sqlite.Message.Contains("backup_job_queue_full", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEnqueueUniqueConflict(DbUpdateException exception)
    {
        if (exception.InnerException is not Microsoft.Data.Sqlite.SqliteException sqlite ||
            sqlite.SqliteErrorCode != 19 ||
            sqlite.SqliteExtendedErrorCode != 2067)
        {
            return false;
        }

        return sqlite.Message.Contains("BackupJobs.RequestId", StringComparison.OrdinalIgnoreCase) ||
               sqlite.Message.Contains("BackupJobs.DatabaseEndpointId", StringComparison.OrdinalIgnoreCase);
    }

    private static string ValidateStage(string stage)
    {
        stage = stage?.Trim() ?? string.Empty;
        if (stage.Length is < 1 or > 32)
            throw new ArgumentException("Stage must contain between 1 and 32 characters.", nameof(stage));
        return stage;
    }

    private static (string Code, string Message) ValidateError(string code, string message)
    {
        code = code?.Trim() ?? string.Empty;
        message = message?.Trim() ?? string.Empty;
        if (code.Length is < 1 or > 100)
            throw new ArgumentException("Error code must contain between 1 and 100 characters.", nameof(code));
        if (message.Length is < 1 or > 2000)
            throw new ArgumentException("Error message must contain between 1 and 2000 characters.", nameof(message));
        return (code, message);
    }

    private static BackupJobSnapshot Snapshot(BackupJob job) => new(
        job.Id,
        job.RequestId,
        job.DatabaseEndpointId,
        job.Status,
        job.Stage,
        job.Percent,
        job.CreatedAtUtc,
        job.StartedAtUtc,
        job.UpdatedAtUtc,
        job.CompletedAtUtc,
        job.ExecutionToken,
        job.BackupRecordId,
        job.ErrorCode,
        job.ErrorMessage);
}
