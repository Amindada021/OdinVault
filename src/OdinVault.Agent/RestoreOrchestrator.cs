using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Database.SqlServer;
using OdinVault.Persistence;

namespace OdinVault.Agent;

public sealed class RestoreOrchestrator(
    OdinVaultDbContext db,
    ISecretProtector protector,
    SqlServerRestoreService restore,
    BackupExecutionCoordinator executionCoordinator)
{
    public async Task<RestorePreflightResponse> PreflightAsync(
        Guid backupId,
        string targetDatabaseName,
        CancellationToken cancellationToken = default)
    {
        var (backup, endpoint) = await LoadRestoreSourceAsync(backupId, cancellationToken);
        var connection = ToConnectionInfo(endpoint);

        var result = await restore.PreflightAsync(
            connection,
            backup.FilePath,
            targetDatabaseName.Trim(),
            cancellationToken);

        return new RestorePreflightResponse(
            backup.Id,
            endpoint.Id,
            endpoint.Name,
            endpoint.DatabaseName,
            result.TargetDatabaseName,
            backup.FileName,
            backup.SizeBytes,
            backup.VerificationStatus,
            result.ProductVersion,
            result.DataDirectory,
            result.LogDirectory,
            result.Files.Select(x => new RestoreFilePlanResponse(
                x.LogicalName,
                x.Type.ToString(),
                x.TargetPath)).ToArray());
    }

    public async Task<RestoreExecutionResponse> RestoreAsync(
        Guid backupId,
        string targetDatabaseName,
        CancellationToken cancellationToken = default)
    {
        var (backup, endpoint) = await LoadRestoreSourceAsync(backupId, cancellationToken);

        await using var lease = await executionCoordinator.TryAcquireAsync(
            endpoint,
            cancellationToken);
        if (lease is null)
            throw new InvalidOperationException("A backup or restore operation is already running for this SQL database.");

        var result = await restore.RestoreToNewDatabaseAsync(
            ToConnectionInfo(endpoint),
            backup.FilePath,
            targetDatabaseName.Trim(),
            cancellationToken);

        return new RestoreExecutionResponse(
            backup.Id,
            endpoint.Id,
            result.TargetDatabaseName,
            result.CompletedAtUtc,
            result.Duration.TotalSeconds);
    }

    private async Task<(BackupRecord Backup, DatabaseEndpoint Endpoint)> LoadRestoreSourceAsync(
        Guid backupId,
        CancellationToken cancellationToken)
    {
        var backup = await db.BackupRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == backupId, cancellationToken)
            ?? throw new KeyNotFoundException("Backup was not found.");

        if (backup.Status != BackupStatus.Succeeded)
            throw new InvalidOperationException("Only successful backups can be restored.");
        if (!backup.LocalFileAvailable ||
            string.IsNullOrWhiteSpace(backup.FilePath) ||
            !File.Exists(backup.FilePath))
        {
            throw new InvalidOperationException(
                "Restore-to-new-DB currently requires the local backup file to be available on the Agent.");
        }

        var endpoint = await db.DatabaseEndpoints
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == backup.DatabaseEndpointId, cancellationToken)
            ?? throw new KeyNotFoundException("Source database endpoint was not found.");

        if (endpoint.Engine != DatabaseEngine.SqlServer)
            throw new NotSupportedException("Restore currently supports SQL Server only.");

        return (backup, endpoint);
    }

    private DatabaseConnectionInfo ToConnectionInfo(DatabaseEndpoint endpoint) =>
        new(
            endpoint.Host,
            endpoint.Port,
            endpoint.DatabaseName,
            endpoint.Username,
            string.IsNullOrWhiteSpace(endpoint.ProtectedPassword)
                ? null
                : protector.Unprotect(endpoint.ProtectedPassword),
            endpoint.TrustServerCertificate);
}

public sealed record RestorePreflightResponse(
    Guid BackupId,
    Guid DatabaseEndpointId,
    string EndpointName,
    string SourceDatabaseName,
    string TargetDatabaseName,
    string BackupFileName,
    long? BackupSizeBytes,
    VerificationStatus VerificationStatus,
    string ProductVersion,
    string DataDirectory,
    string LogDirectory,
    IReadOnlyList<RestoreFilePlanResponse> Files);

public sealed record RestoreFilePlanResponse(
    string LogicalName,
    string Type,
    string TargetPath);

public sealed record RestoreExecutionResponse(
    Guid BackupId,
    Guid DatabaseEndpointId,
    string TargetDatabaseName,
    DateTime CompletedAtUtc,
    double DurationSeconds);
