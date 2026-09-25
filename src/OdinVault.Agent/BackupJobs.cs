using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;

namespace OdinVault.Agent;

// Jobs belong to the service, never to the phone's HTTP connection.
public sealed class BackupJobs(IServiceScopeFactory scopes, ILogger<BackupJobs> logger) : BackgroundService
{
    private readonly Channel<Job> queue = Channel.CreateBounded<Job>(64);
    private readonly ConcurrentDictionary<Guid, Job> jobs = new();
    private readonly Dictionary<Guid, Guid> active = new();
    private readonly object gate = new();

    public Job? Start(Guid databaseId)
    {
        lock (gate)
        {
            if (active.TryGetValue(databaseId, out var id) && jobs.TryGetValue(id, out var existing)) return existing;
            foreach (var old in jobs.Where(x => x.Value.FinishedAt < DateTime.UtcNow.AddDays(-1))) jobs.TryRemove(old.Key, out _);
            var job = new Job(databaseId);
            jobs[job.Id] = job;
            active[databaseId] = job.Id;
            if (queue.Writer.TryWrite(job)) return job;
            jobs.TryRemove(job.Id, out _);
            active.Remove(databaseId);
            return null;
        }
    }

    public Job? Get(Guid id) => jobs.GetValueOrDefault(id);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var backup = await scope.ServiceProvider.GetRequiredService<BackupOrchestrator>()
                    .RunNowAsync(job.DatabaseId, stoppingToken, job);
                job.Report(new BackupProgress("complete", 100, backup));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Backup job {JobId} failed.", job.Id);
                job.Fail("عملیات بکاپ کامل نشد؛ جزئیات را در تاریخچه بکاپ سرور بررسی کنید.");
            }
            finally
            {
                job.FinishedAt = DateTime.UtcNow;
                lock (gate) active.Remove(job.DatabaseId);
            }
        }
    }

    public sealed class Job(Guid databaseId) : IProgress<BackupProgress>
    {
        private readonly object sync = new();
        private string stage = "queued";
        private int? percent;
        private BackupRecord? backup;
        private string? error;
        public Guid Id { get; } = Guid.NewGuid();
        public Guid DatabaseId { get; } = databaseId;
        public DateTime? FinishedAt { get; set; }
        public void Report(BackupProgress value)
        {
            lock (sync) { stage = value.Stage; percent = value.Percent; backup = value.Backup ?? backup; }
        }
        public void Fail(string message) { lock (sync) { stage = "failed"; error = message; } }
        public object Snapshot() { lock (sync) return new { Id, DatabaseId, Stage = stage, Percent = percent, Backup = backup, Error = error }; }
    }
}

public static class BackupJobEndpoints
{
    public static void MapBackupJobs(this WebApplication app)
    {
        app.MapPost("/api/databases/{id:guid}/backup-jobs", async (Guid id, OdinVaultDbContext db, BackupJobs jobs, CancellationToken ct) =>
        {
            var endpoint = await db.DatabaseEndpoints.FirstOrDefaultAsync(x => x.Id == id, ct);
            var policy = await db.BackupPolicies.FirstOrDefaultAsync(x => x.DatabaseEndpointId == id, ct);
            if (endpoint is null) return Results.NotFound();
            if (!endpoint.IsEnabled || policy?.IsEnabled != true) return Results.BadRequest(new { message = "دیتابیس یا زمان‌بندی غیرفعال است." });
            var job = jobs.Start(id);
            return job is null ? Results.StatusCode(429) : Results.Accepted($"/api/backup-jobs/{job.Id}", job.Snapshot());
        });
        app.MapGet("/api/backup-jobs/{id:guid}", (Guid id, BackupJobs jobs) =>
            jobs.Get(id) is { } job ? Results.Ok(job.Snapshot()) : Results.NotFound(new { message = "عملیات پیدا نشد؛ تاریخچه بکاپ را بررسی کنید." }));
    }
}
