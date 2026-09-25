using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;

namespace OdinVault.Agent;

// Jobs belong to the service, never to the phone's HTTP connection.
public sealed class BackupJobs(
    IServiceScopeFactory scopes,
    ILogger<BackupJobs> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            BackupJobSnapshot? job = null;

            try
            {
                await using (var claimScope = scopes.CreateAsyncScope())
                {
                    var store = claimScope.ServiceProvider.GetRequiredService<IBackupJobStore>();
                    var claim = await store.TryClaimNextAsync(stoppingToken);
                    if (claim.Status == BackupJobMutationStatus.NotFound)
                    {
                        await Task.Delay(500, stoppingToken);
                        continue;
                    }

                    if (claim.Status != BackupJobMutationStatus.Updated || claim.Job is null)
                    {
                        await Task.Delay(100, stoppingToken);
                        continue;
                    }

                    job = claim.Job;
                }

                if (job.ExecutionToken is not Guid executionToken)
                    throw new InvalidOperationException($"Backup job {job.Id} was claimed without an execution token.");

                await using var executionScope = scopes.CreateAsyncScope();
                var orchestrator = executionScope.ServiceProvider.GetRequiredService<BackupOrchestrator>();
                await using var progress = new PersistentBackupProgress(
                    scopes,
                    job.Id,
                    executionToken,
                    logger);

                var backup = await orchestrator.RunNowAsync(
                    job.DatabaseEndpointId,
                    stoppingToken,
                    progress,
                    async (record, ct) =>
                    {
                        await using var linkScope = scopes.CreateAsyncScope();
                        var linkStore = linkScope.ServiceProvider.GetRequiredService<IBackupJobStore>();
                        var linked = await linkStore.AttachBackupRecordAsync(
                            job.Id,
                            executionToken,
                            record.Id,
                            "backup",
                            0,
                            ct);

                        if (linked.Status != BackupJobMutationStatus.Updated)
                            throw new InvalidOperationException(
                                $"Could not durably link backup record {record.Id} to job {job.Id}: {linked.Status}.");
                    });

                await progress.FlushAsync();

                await using var completionScope = scopes.CreateAsyncScope();
                var completionStore = completionScope.ServiceProvider.GetRequiredService<IBackupJobStore>();
                var completed = await completionStore.CompleteAsync(
                    job.Id,
                    executionToken,
                    backup.Id,
                    CancellationToken.None);

                if (completed.Status != BackupJobMutationStatus.Updated)
                {
                    logger.LogError(
                        "Backup job {JobId} completed locally but durable job completion returned {Status}.",
                        job.Id,
                        completed.Status);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                if (job?.ExecutionToken is Guid executionToken)
                    await FinalizeOnShutdownAsync(job.Id, executionToken);

                break;
            }
            catch (Exception ex)
            {
                if (job is null)
                {
                    logger.LogError(ex, "Backup worker failed before a durable job was claimed.");
                    await DelayAfterFailureAsync(stoppingToken);
                    continue;
                }

                logger.LogError(ex, "Backup job {JobId} failed.", job.Id);

                if (job.ExecutionToken is Guid executionToken)
                    await TryFailAsync(job.Id, executionToken, "backup_failed", "عملیات بکاپ کامل نشد؛ جزئیات را در تاریخچه بکاپ سرور بررسی کنید.");
            }
        }
    }

    private async Task FinalizeOnShutdownAsync(Guid jobId, Guid executionToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IBackupJobStore>();
            var current = await store.GetAsync(jobId, CancellationToken.None);

            if (current?.BackupRecordId is Guid backupRecordId)
            {
                var completed = await store.CompleteAsync(
                    jobId,
                    executionToken,
                    backupRecordId,
                    CancellationToken.None);

                if (completed.Status == BackupJobMutationStatus.Updated)
                {
                    logger.LogInformation(
                        "Backup job {JobId} was finalized as succeeded during service shutdown because its local backup had already completed.",
                        jobId);
                    return;
                }

                if (completed.Status == BackupJobMutationStatus.StateConflict &&
                    completed.Job?.Status == BackupJobStatus.Succeeded)
                {
                    return;
                }
            }

            var interrupted = await store.InterruptAsync(
                jobId,
                executionToken,
                "service_stopping",
                "سرویس هنگام اجرای بکاپ متوقف شد.",
                CancellationToken.None);

            if (interrupted.Status is not BackupJobMutationStatus.Updated and not BackupJobMutationStatus.StateConflict)
            {
                logger.LogWarning(
                    "Could not persist shutdown state for backup job {JobId}: {Status}.",
                    jobId,
                    interrupted.Status);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not finalize backup job {JobId} during service shutdown.", jobId);
        }
    }

    private async Task TryFailAsync(Guid jobId, Guid executionToken, string code, string message)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IBackupJobStore>();
            var result = await store.FailAsync(jobId, executionToken, code, message, CancellationToken.None);
            if (result.Status is not BackupJobMutationStatus.Updated and not BackupJobMutationStatus.StateConflict)
                logger.LogWarning("Could not persist failure for backup job {JobId}: {Status}.", jobId, result.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not persist failure for backup job {JobId}.", jobId);
        }
    }

    private async Task TryInterruptAsync(Guid jobId, Guid executionToken, string code, string message)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IBackupJobStore>();
            var result = await store.InterruptAsync(jobId, executionToken, code, message, CancellationToken.None);
            if (result.Status is not BackupJobMutationStatus.Updated and not BackupJobMutationStatus.StateConflict)
                logger.LogWarning("Could not persist interruption for backup job {JobId}: {Status}.", jobId, result.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not persist interruption for backup job {JobId}.", jobId);
        }
    }

    private static async Task DelayAfterFailureAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(1000, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private sealed class PersistentBackupProgress : IProgress<BackupProgress>, IAsyncDisposable
    {
        private readonly IServiceScopeFactory scopes;
        private readonly Guid jobId;
        private readonly Guid executionToken;
        private readonly ILogger logger;
        private readonly Channel<BackupProgress> channel = Channel.CreateUnbounded<BackupProgress>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private readonly Task pump;
        private int completed;

        public PersistentBackupProgress(
            IServiceScopeFactory scopes,
            Guid jobId,
            Guid executionToken,
            ILogger logger)
        {
            this.scopes = scopes;
            this.jobId = jobId;
            this.executionToken = executionToken;
            this.logger = logger;
            pump = PumpAsync();
        }

        public void Report(BackupProgress value)
        {
            if (Volatile.Read(ref completed) != 0)
                return;

            if (!channel.Writer.TryWrite(value))
                logger.LogWarning("Could not queue progress update for backup job {JobId}.", jobId);
        }

        public async Task FlushAsync()
        {
            if (Interlocked.Exchange(ref completed, 1) == 0)
                channel.Writer.TryComplete();

            await pump;
        }

        public async ValueTask DisposeAsync()
        {
            await FlushAsync();
        }

        private async Task PumpAsync()
        {
            await foreach (var progress in channel.Reader.ReadAllAsync())
            {
                await using var scope = scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IBackupJobStore>();
                var result = progress.Backup is { } backup
                    ? await store.AttachBackupRecordAsync(
                        jobId,
                        executionToken,
                        backup.Id,
                        progress.Stage,
                        progress.Percent,
                        CancellationToken.None)
                    : await store.UpdateProgressAsync(
                        jobId,
                        executionToken,
                        progress.Stage,
                        progress.Percent,
                        CancellationToken.None);

                if (result.Status != BackupJobMutationStatus.Updated)
                    throw new InvalidOperationException(
                        $"Durable progress update for backup job {jobId} failed with {result.Status}.");
            }
        }
    }
}

public static class BackupJobEndpoints
{
    public static void MapBackupJobs(this WebApplication app)
    {
        app.MapPost("/api/databases/{id:guid}/backup-jobs", async (
            Guid id,
            HttpRequest request,
            OdinVaultDbContext db,
            IBackupJobStore store,
            CancellationToken ct) =>
        {
            var endpoint = await db.DatabaseEndpoints.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            var policy = await db.BackupPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.DatabaseEndpointId == id, ct);
            if (endpoint is null)
                return Results.NotFound(new { message = "دیتابیس پیدا نشد." });
            if (!endpoint.IsEnabled || policy?.IsEnabled != true)
                return Results.BadRequest(new { message = "دیتابیس یا زمان‌بندی غیرفعال است." });

            var requestId = TryGetRequestId(request) ?? Guid.NewGuid();
            var enqueue = await store.EnqueueAsync(requestId, id, ct);

            if (enqueue.Status == BackupJobEnqueueStatus.RequestConflict)
                return Results.Conflict(new { message = "شناسه درخواست قبلاً برای دیتابیس دیگری استفاده شده است." });
            if (enqueue.Status == BackupJobEnqueueStatus.QueueFull)
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);

            var job = enqueue.Job;
            if (job is null)
                return Results.StatusCode(StatusCodes.Status500InternalServerError);

            return Results.Accepted(
                $"/api/backup-jobs/{job.Id}",
                await ToApiSnapshotAsync(job, db, ct));
        });

        app.MapGet("/api/backup-jobs/{id:guid}", async (
            Guid id,
            IBackupJobStore store,
            OdinVaultDbContext db,
            CancellationToken ct) =>
        {
            var job = await store.GetAsync(id, ct);
            if (job is null)
                return Results.NotFound(new { message = "عملیات پیدا نشد؛ تاریخچه بکاپ را بررسی کنید." });

            return Results.Ok(await ToApiSnapshotAsync(job, db, ct));
        });
    }

    private static Guid? TryGetRequestId(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("X-OdinVault-Request-Id", out var values))
            return null;

        return Guid.TryParse(values.FirstOrDefault(), out var requestId) && requestId != Guid.Empty
            ? requestId
            : null;
    }

    private static async Task<object> ToApiSnapshotAsync(
        BackupJobSnapshot job,
        OdinVaultDbContext db,
        CancellationToken cancellationToken)
    {
        BackupRecord? backup = null;
        if (job.BackupRecordId is Guid backupRecordId)
        {
            backup = await db.BackupRecords.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == backupRecordId && x.Status == BackupStatus.Succeeded,
                    cancellationToken);
        }

        return new
        {
            job.Id,
            DatabaseId = job.DatabaseEndpointId,
            Stage = ToLegacyStage(job.Status, job.Stage),
            job.Percent,
            Backup = backup,
            Error = job.ErrorMessage
        };
    }

    private static string ToLegacyStage(BackupJobStatus status, string stage) => status switch
    {
        BackupJobStatus.Queued => "queued",
        BackupJobStatus.Failed or BackupJobStatus.Interrupted => "failed",
        BackupJobStatus.Succeeded => "complete",
        _ => stage
    };
}
