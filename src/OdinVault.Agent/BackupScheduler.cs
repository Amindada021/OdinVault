using System.Buffers.Binary;
using System.Security.Cryptography;
using Cronos;
using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;

namespace OdinVault.Agent;

public sealed class BackupScheduler(
    IServiceScopeFactory scopeFactory,
    ILogger<BackupScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OdinVault backup scheduler started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnqueueDueBackupsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected scheduler iteration failure.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task EnqueueDueBackupsAsync(CancellationToken cancellationToken)
    {
        List<Guid> policyIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OdinVaultDbContext>();
            policyIds = await db.BackupPolicies
                .Where(x => x.IsEnabled && x.ScheduleCron != null && x.ScheduleCron != string.Empty)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        }

        foreach (var policyId in policyIds)
        {
            try
            {
                await EnqueuePolicyIfDueAsync(policyId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled backup evaluation failed for policy {PolicyId}.", policyId);
            }
        }
    }

    private async Task EnqueuePolicyIfDueAsync(Guid policyId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OdinVaultDbContext>();
        var jobs = scope.ServiceProvider.GetRequiredService<IBackupJobStore>();

        var policy = await db.BackupPolicies.FirstOrDefaultAsync(x => x.Id == policyId, cancellationToken);
        if (policy is null || !policy.IsEnabled || string.IsNullOrWhiteSpace(policy.ScheduleCron))
            return;

        var databaseEnabled = await db.DatabaseEndpoints
            .AnyAsync(x => x.Id == policy.DatabaseEndpointId && x.IsEnabled, cancellationToken);
        if (!databaseEnabled)
            return;

        CronExpression expression;
        try
        {
            expression = CronExpression.Parse(policy.ScheduleCron, CronFormat.Standard);
        }
        catch (CronFormatException ex)
        {
            logger.LogWarning(ex, "Invalid cron expression {Cron} for policy {PolicyId}.", policy.ScheduleCron, policy.Id);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var searchFrom = now.AddDays(-7);
        var previous = expression
            .GetOccurrences(searchFrom, now, TimeZoneInfo.Utc, fromInclusive: true, toInclusive: true)
            .LastOrDefault();

        if (previous == default)
            return;

        var occurrenceUtc = previous.UtcDateTime;
        if (policy.LastScheduledRunUtc.HasValue && policy.LastScheduledRunUtc.Value >= occurrenceUtc)
            return;

        var requestId = CreateScheduledRequestId(policy.Id, occurrenceUtc);
        var enqueue = await jobs.EnqueueAsync(requestId, policy.DatabaseEndpointId, cancellationToken);

        if (enqueue.Status == BackupJobEnqueueStatus.ActiveConflict)
        {
            logger.LogInformation(
                "Scheduled backup for database {DatabaseId} is still due for cron slot {OccurrenceUtc}, but another job {JobId} is active.",
                policy.DatabaseEndpointId,
                occurrenceUtc,
                enqueue.ConflictingJobId);
            return;
        }

        if (enqueue.Status == BackupJobEnqueueStatus.RequestConflict)
        {
            logger.LogError(
                "Scheduled request id collision for policy {PolicyId}, database {DatabaseId}, cron slot {OccurrenceUtc}.",
                policy.Id,
                policy.DatabaseEndpointId,
                occurrenceUtc);
            return;
        }

        if (enqueue.Job is null)
        {
            logger.LogError(
                "Scheduled backup enqueue returned {Status} without a job for policy {PolicyId}.",
                enqueue.Status,
                policy.Id);
            return;
        }

        // Created means this run persisted the job. Existing means a previous run already
        // persisted this exact cron slot, which makes restart between enqueue and this save safe.
        if (enqueue.Status is not BackupJobEnqueueStatus.Created and not BackupJobEnqueueStatus.Existing)
        {
            logger.LogWarning(
                "Scheduled backup cron slot {OccurrenceUtc} was not acknowledged. Enqueue status: {Status}.",
                occurrenceUtc,
                enqueue.Status);
            return;
        }

        policy.LastScheduledRunUtc = occurrenceUtc;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Scheduled backup queued for database {DatabaseId}. Cron slot: {OccurrenceUtc}; Job: {JobId}; Enqueue: {Status}.",
            policy.DatabaseEndpointId,
            occurrenceUtc,
            enqueue.Job.Id,
            enqueue.Status);
    }

    private static Guid CreateScheduledRequestId(Guid policyId, DateTime occurrenceUtc)
    {
        Span<byte> input = stackalloc byte[24];
        policyId.TryWriteBytes(input[..16]);
        BinaryPrimitives.WriteInt64LittleEndian(input[16..], occurrenceUtc.ToUniversalTime().Ticks);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }
}
