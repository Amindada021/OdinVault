using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;

namespace OdinVault.Agent;

public sealed class BackupOrchestrator(
    OdinVaultDbContext db,
    IEnumerable<IDatabaseBackupProvider> providers,
    ISecretProtector secretProtector,
    ILogger<BackupOrchestrator> logger)
{
    public async Task<BackupRecord> RunNowAsync(Guid databaseId, CancellationToken cancellationToken = default)
    {
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
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        db.BackupRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var connection = ToConnectionInfo(endpoint);
            var result = await provider.CreateBackupAsync(
                new BackupExecutionRequest(endpoint.Id, connection, policy.BackupDirectory, policy.VerifyAfterBackup),
                cancellationToken);

            record.FileName = result.FileName;
            record.FilePath = result.FilePath;
            record.SizeBytes = result.SizeBytes;
            record.VerificationStatus = result.VerificationStatus;
            record.Status = BackupStatus.Succeeded;
            record.CompletedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            await CleanupRetentionAsync(endpoint.Id, policy.MaxLocalBackups, cancellationToken);
            return record;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backup failed for database {DatabaseId}.", endpoint.Id);
            record.Status = BackupStatus.Failed;
            record.VerificationStatus = record.VerificationStatus == VerificationStatus.Pending
                ? VerificationStatus.Failed
                : record.VerificationStatus;
            record.CompletedAtUtc = DateTimeOffset.UtcNow;
            record.Error = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
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
        if (maxBackups < 1) return;

        var oldRecords = await db.BackupRecords
            .Where(x => x.DatabaseEndpointId == databaseId && x.Status == BackupStatus.Succeeded)
            .OrderByDescending(x => x.CompletedAtUtc)
            .Skip(maxBackups)
            .ToListAsync(cancellationToken);

        foreach (var old in oldRecords)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(old.FilePath) && File.Exists(old.FilePath))
                    File.Delete(old.FilePath);
                db.BackupRecords.Remove(old);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not remove expired backup {BackupId}.", old.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
