using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;

namespace OdinVault.Agent;

public sealed class BackupOrchestrator(
    OdinVaultDbContext db,
    IEnumerable<IDatabaseBackupProvider> providers,
    ISecretProtector secretProtector,
    BackupExecutionCoordinator executionCoordinator,
    StorageReplicationService replicationService,
    ILogger<BackupOrchestrator> logger)
{
    public async Task<BackupRecord> RunNowAsync(Guid databaseId, CancellationToken cancellationToken = default, IProgress<BackupProgress>? progress = null)
    {
        await using var lease = await executionCoordinator.TryAcquireAsync(databaseId, cancellationToken);
        if (lease is null)
            throw new InvalidOperationException("A backup is already running for this database.");

        var endpoint = await db.DatabaseEndpoints.FirstOrDefaultAsync(x => x.Id == databaseId, cancellationToken)
            ?? throw new KeyNotFoundException("Database endpoint was not found.");
        var policy = await db.BackupPolicies.FirstOrDefaultAsync(x => x.DatabaseEndpointId == databaseId, cancellationToken)
            ?? throw new InvalidOperationException("Backup policy is not configured for this database.");

        if (!endpoint.IsEnabled || !policy.IsEnabled)
            throw new InvalidOperationException("Database or backup policy is disabled.");

        var provider = providers.FirstOrDefault(x => x.Engine == endpoint.Engine)
            ?? throw new NotSupportedException($"No backup provider is registered for {endpoint.Engine}.");

        var record = new BackupRecord
        {
            DatabaseEndpointId = endpoint.Id,
            Status = BackupStatus.Running,
            VerificationStatus = policy.VerifyAfterBackup ? VerificationStatus.Pending : VerificationStatus.NotRequested,
            StartedAtUtc = DateTime.UtcNow
        };

        db.BackupRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        BackupExecutionResult result;
        try
        {
            var connection = ToConnectionInfo(endpoint);
            result = await provider.CreateBackupAsync(
                new BackupExecutionRequest(endpoint.Id, connection, policy.BackupDirectory, policy.VerifyAfterBackup, progress),
                cancellationToken);

            record.FileName = result.FileName;
            record.FilePath = result.FilePath;
            record.LocalFileAvailable = true;
            record.SizeBytes = result.SizeBytes;
            record.VerificationStatus = result.VerificationStatus;
            record.Status = BackupStatus.Succeeded;
            record.CompletedAtUtc = DateTime.UtcNow;
            record.Error = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backup failed for database {DatabaseId}.", endpoint.Id);
            record.Status = BackupStatus.Failed;
            record.VerificationStatus = record.VerificationStatus == VerificationStatus.Pending
                ? VerificationStatus.Failed
                : record.VerificationStatus;
            record.CompletedAtUtc = DateTime.UtcNow;
            record.Error = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        if (result.VerificationStatus == VerificationStatus.Failed)
        {
            logger.LogWarning(
                "Backup {BackupId} completed but verification failed. {VerificationError}",
                record.Id,
                result.VerificationError);
        }

        // The local file is ready: clients may download while downstream work continues.
        progress?.Report(new BackupProgress("replicating", null, record));
        try
        {
            await replicationService.ReplicateAsync(record, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Replication processing failed after local backup {BackupId} succeeded.", record.Id);
        }

        progress?.Report(new BackupProgress("retention", null, record));
        try
        {
            await CleanupRetentionAsync(endpoint.Id, policy.MaxLocalBackups, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Retention cleanup failed after local backup {BackupId} succeeded.", record.Id);
        }

        return record;
    }

    public async Task TestConnectionAsync(Guid databaseId, CancellationToken cancellationToken = default)
    {
        var endpoint = await db.DatabaseEndpoints.FirstOrDefaultAsync(x => x.Id == databaseId, cancellationToken)
            ?? throw new KeyNotFoundException("Database endpoint was not found.");
        var provider = providers.FirstOrDefault(x => x.Engine == endpoint.Engine)
            ?? throw new NotSupportedException($"No backup provider is registered for {endpoint.Engine}.");
        await provider.TestConnectionAsync(ToConnectionInfo(endpoint), cancellationToken);
    }

    private DatabaseConnectionInfo ToConnectionInfo(DatabaseEndpoint endpoint) => new(
        endpoint.Host,
        endpoint.Port,
        endpoint.DatabaseName,
        endpoint.Username,
        string.IsNullOrWhiteSpace(endpoint.ProtectedPassword) ? null : secretProtector.Unprotect(endpoint.ProtectedPassword),
        endpoint.TrustServerCertificate);

    private async Task CleanupRetentionAsync(Guid databaseId, int maxBackups, CancellationToken cancellationToken)
    {
        if (maxBackups < 1)
            return;

        var oldRecords = await db.BackupRecords
            .Where(x => x.DatabaseEndpointId == databaseId &&
                        x.Status == BackupStatus.Succeeded &&
                        x.LocalFileAvailable)
            .OrderByDescending(x => x.CompletedAtUtc)
            .Skip(maxBackups)
            .ToListAsync(cancellationToken);

        foreach (var old in oldRecords)
        {
            if (await db.BackupReplicas.AnyAsync(x => x.BackupRecordId == old.Id && x.Status != ReplicaStatus.Succeeded, cancellationToken))
                continue;
            try
            {
                if (!string.IsNullOrWhiteSpace(old.FilePath) && File.Exists(old.FilePath))
                    File.Delete(old.FilePath);

                old.LocalFileAvailable = false;
                old.LocalFileDeletedAtUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not remove expired local backup {BackupId}.", old.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
