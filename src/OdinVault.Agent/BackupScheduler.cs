using Cronos;
using Microsoft.EntityFrameworkCore;
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
                await RunDueBackupsAsync(stoppingToken);
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

    private async Task RunDueBackupsAsync(CancellationToken cancellationToken)
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
                await RunPolicyIfDueAsync(policyId, cancellationToken);
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

    private async Task RunPolicyIfDueAsync(Guid policyId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OdinVaultDbContext>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<BackupOrchestrator>();

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

        // Persist the cron slot before running so a failed backup is not duplicated every 30 seconds.
        policy.LastScheduledRunUtc = occurrenceUtc;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Running scheduled backup for database {DatabaseId}. Cron slot: {OccurrenceUtc}.",
            policy.DatabaseEndpointId,
            occurrenceUtc);

        try
        {
            await orchestrator.RunNowAsync(policy.DatabaseEndpointId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduled backup failed for database {DatabaseId}.", policy.DatabaseEndpointId);
        }
    }
}
