using Cronos;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OdinVault.Agent;
using OdinVault.Core;
using OdinVault.Database.SqlServer;
using OdinVault.Persistence;
using OdinVault.Storage.GoogleDrive;
using OdinVault.Storage.Local;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options => options.ServiceName = "OdinVault Agent");

string ResolvePath(string? configured, string fallback)
{
    var value = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
    return Path.IsPathRooted(value)
        ? Path.GetFullPath(value)
        : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, value));
}

var dataDirectory = ResolvePath(
    builder.Configuration["OdinVault:DataDirectory"],
    OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OdinVault")
        : "data");
var storageDirectory = ResolvePath(builder.Configuration["OdinVault:StorageDirectory"], Path.Combine(dataDirectory, "storage"));
var replicaDirectory = ResolvePath(builder.Configuration["OdinVault:ReplicaDirectory"], Path.Combine(dataDirectory, "replicas"));
Directory.CreateDirectory(dataDirectory);
Directory.CreateDirectory(storageDirectory);
Directory.CreateDirectory(replicaDirectory);
Directory.CreateDirectory(Path.Combine(dataDirectory, "keys"));

var agentApiKey = AgentApiKeyFactory.LoadOrCreate(
    dataDirectory,
    builder.Configuration["OdinVault:ApiKey"] ?? Environment.GetEnvironmentVariable("ODINVAULT_API_KEY"));

var googleDriveOptions = new GoogleDriveOAuthOptions(
    builder.Configuration["OdinVault:GoogleDrive:ClientId"] ?? Environment.GetEnvironmentVariable("ODINVAULT_GOOGLE_CLIENT_ID") ?? string.Empty,
    builder.Configuration["OdinVault:GoogleDrive:ClientSecret"] ?? Environment.GetEnvironmentVariable("ODINVAULT_GOOGLE_CLIENT_SECRET") ?? string.Empty);

builder.Services.AddSingleton(agentApiKey);
builder.Services.AddSingleton(new ReplicaSettings(dataDirectory, replicaDirectory));
builder.Services.AddSingleton(googleDriveOptions);
builder.Services.AddSingleton<GoogleDriveOAuthService>();
builder.Services.AddSingleton<GoogleDrivePairingStateStore>();
builder.Services.AddHttpClient("OdinVaultReplica", client => client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddOpenApi();
builder.Services.AddDbContext<OdinVaultDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "odinvault.db")}"));

builder.Services
    .AddDataProtection()
    .SetApplicationName("OdinVault")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));

builder.Services.AddScoped<ISecretProtector, SecretProtector>();
builder.Services.AddScoped<IBackupJobStore, SqliteBackupJobStore>();
builder.Services.AddScoped<SqlServerDiscoveryService>();
builder.Services.AddScoped<IDatabaseBackupProvider, SqlServerBackupProvider>();
builder.Services.AddSingleton<IBackupStorageProvider>(_ => new LocalBackupStorage(storageDirectory));
builder.Services.AddSingleton<BackupExecutionCoordinator>();
builder.Services.AddScoped<StorageReplicationService>();
builder.Services.AddScoped<BackupOrchestrator>();
builder.Services.AddScoped<BackupJobRecoveryService>();
builder.Services.AddHostedService<BackupScheduler>();
builder.Services.AddHostedService<ReplicationRetryWorker>();
builder.Services.AddSingleton<BackupJobs>();
builder.Services.AddHostedService<BackupJobs>(sp => sp.GetRequiredService<BackupJobs>());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OdinVaultDbContext>();
    await SqliteMigrationBootstrapper.PrepareAsync(db);
    await db.Database.MigrateAsync();

    var recovery = scope.ServiceProvider.GetRequiredService<BackupJobRecoveryService>();
    await recovery.RecoverAsync();
}

app.Logger.LogInformation(
    "OdinVault Agent started. Data: {DataDirectory}; Local storage: {StorageDirectory}; Replica storage: {ReplicaDirectory}; API key file: {ApiKeyPath}; Google Drive OAuth configured: {GoogleConfigured}",
    dataDirectory,
    storageDirectory,
    replicaDirectory,
    Path.Combine(dataDirectory, "agent-api-key.txt"),
    !string.IsNullOrWhiteSpace(googleDriveOptions.ClientId) && !string.IsNullOrWhiteSpace(googleDriveOptions.ClientSecret));

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseMiddleware<AgentApiKeyMiddleware>();

app.MapGet("/api/health", () => Results.Ok(new
{
    service = "OdinVault.Agent",
    status = "healthy",
    utc = DateTime.UtcNow
}));

app.MapPost("/api/sql-server/discover", async (
    DiscoverSqlServerRequest request,
    SqlServerDiscoveryService discovery,
    OdinVaultDbContext db,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Host))
        return Results.BadRequest(new { message = "host is required." });

    try
    {
        var discovered = await discovery.DiscoverAsync(
            request.Host,
            request.Port,
            request.Username,
            request.Password,
            request.TrustServerCertificate,
            ct);

        var registered = await db.DatabaseEndpoints
            .Where(x => x.Engine == DatabaseEngine.SqlServer && x.Host == request.Host.Trim())
            .Select(x => x.DatabaseName)
            .ToListAsync(ct);

        var registeredNames = registered.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = discovered.Select(x => new
        {
            x.Name,
            x.DatabaseId,
            x.State,
            x.RecoveryModel,
            x.IsSystem,
            x.HasAccess,
            isRegistered = registeredNames.Contains(x.Name),
            canBackup = !x.IsSystem && x.HasAccess && string.Equals(x.State, "ONLINE", StringComparison.OrdinalIgnoreCase)
        });

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapGet("/api/databases", async (OdinVaultDbContext db, CancellationToken ct) =>
{
    var endpoints = await db.DatabaseEndpoints.OrderBy(x => x.Name).ToListAsync(ct);
    var policies = await db.BackupPolicies.ToDictionaryAsync(x => x.DatabaseEndpointId, ct);
    var items = endpoints.Select(x =>
    {
        policies.TryGetValue(x.Id, out var policy);
        return ToDatabaseResponse(x, policy);
    });
    return Results.Ok(items);
});

app.MapGet("/api/databases/{id:guid}", async (Guid id, OdinVaultDbContext db, CancellationToken ct) =>
{
    var endpoint = await db.DatabaseEndpoints.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (endpoint is null)
        return Results.NotFound(new { message = "Database endpoint was not found." });
    var policy = await db.BackupPolicies.FirstOrDefaultAsync(x => x.DatabaseEndpointId == id, ct);
    return Results.Ok(ToDatabaseResponse(endpoint, policy));
});

app.MapPost("/api/databases", async (CreateDatabaseRequest request, OdinVaultDbContext db, ISecretProtector protector, CancellationToken ct) =>
{
    var validationError = ValidateDatabaseRequest(request.Name, request.Host, request.DatabaseName, request.BackupDirectory, request.ScheduleCron);
    if (validationError is not null)
        return Results.BadRequest(new { message = validationError });

    if (await HasDatabaseIdentityConflictAsync(
        db,
        null,
        DatabaseEngine.SqlServer,
        request.Host,
        request.Port,
        request.DatabaseName,
        ct))
    {
        return Results.Conflict(new { message = "این دیتابیس SQL Server قبلاً ثبت شده است." });
    }

    var endpoint = new DatabaseEndpoint
    {
        Name = request.Name.Trim(),
        Engine = DatabaseEngine.SqlServer,
        Host = request.Host.Trim(),
        Port = request.Port,
        DatabaseName = request.DatabaseName.Trim(),
        Username = request.Username?.Trim() ?? string.Empty,
        ProtectedPassword = string.IsNullOrEmpty(request.Password) ? null : protector.Protect(request.Password),
        TrustServerCertificate = request.TrustServerCertificate,
        IsEnabled = request.IsEnabled
    };

    var policy = new BackupPolicy
    {
        DatabaseEndpointId = endpoint.Id,
        BackupDirectory = request.BackupDirectory.Trim(),
        MaxLocalBackups = Math.Clamp(request.MaxLocalBackups, 1, 1000),
        VerifyAfterBackup = request.VerifyAfterBackup,
        ScheduleCron = NormalizeCron(request.ScheduleCron),
        IsEnabled = request.IsEnabled
    };

    db.DatabaseEndpoints.Add(endpoint);
    db.BackupPolicies.Add(policy);
    try
    {
        await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (IsDatabaseIdentityConflict(ex))
    {
        return Results.Conflict(new { message = "این دیتابیس SQL Server قبلاً ثبت شده است." });
    }
    return Results.Created($"/api/databases/{endpoint.Id}", ToDatabaseResponse(endpoint, policy));
});

app.MapPut("/api/databases/{id:guid}", async (Guid id, UpdateDatabaseRequest request, OdinVaultDbContext db, ISecretProtector protector, CancellationToken ct) =>
{
    var endpoint = await db.DatabaseEndpoints.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (endpoint is null)
        return Results.NotFound(new { message = "Database endpoint was not found." });
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.DatabaseName))
        return Results.BadRequest(new { message = "Name, host and databaseName are required." });

    if (await HasDatabaseIdentityConflictAsync(
        db,
        id,
        DatabaseEngine.SqlServer,
        request.Host,
        request.Port,
        request.DatabaseName,
        ct))
    {
        return Results.Conflict(new { message = "این دیتابیس SQL Server قبلاً با یک اتصال دیگر ثبت شده است." });
    }

    endpoint.Name = request.Name.Trim();
    endpoint.Host = request.Host.Trim();
    endpoint.Port = request.Port;
    endpoint.DatabaseName = request.DatabaseName.Trim();
    endpoint.Username = request.Username?.Trim() ?? string.Empty;
    endpoint.TrustServerCertificate = request.TrustServerCertificate;
    endpoint.IsEnabled = request.IsEnabled;
    if (request.ClearPassword)
        endpoint.ProtectedPassword = null;
    else if (request.Password is not null)
        endpoint.ProtectedPassword = protector.Protect(request.Password);

    try
    {
        await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (IsDatabaseIdentityConflict(ex))
    {
        return Results.Conflict(new { message = "این دیتابیس SQL Server قبلاً با یک اتصال دیگر ثبت شده است." });
    }
    var policy = await db.BackupPolicies.FirstOrDefaultAsync(x => x.DatabaseEndpointId == id, ct);
    return Results.Ok(ToDatabaseResponse(endpoint, policy));
});

app.MapPut("/api/databases/{id:guid}/policy", async (Guid id, UpdateBackupPolicyRequest request, OdinVaultDbContext db, CancellationToken ct) =>
{
    if (!await db.DatabaseEndpoints.AnyAsync(x => x.Id == id, ct))
        return Results.NotFound(new { message = "Database endpoint was not found." });
    if (string.IsNullOrWhiteSpace(request.BackupDirectory))
        return Results.BadRequest(new { message = "backupDirectory is required." });
    var cronError = ValidateCron(request.ScheduleCron);
    if (cronError is not null)
        return Results.BadRequest(new { message = cronError });

    var policy = await db.BackupPolicies.FirstOrDefaultAsync(x => x.DatabaseEndpointId == id, ct);
    if (policy is null)
    {
        policy = new BackupPolicy { DatabaseEndpointId = id };
        db.BackupPolicies.Add(policy);
    }

    var normalizedCron = NormalizeCron(request.ScheduleCron);
    if (!string.Equals(policy.ScheduleCron, normalizedCron, StringComparison.Ordinal))
        policy.LastScheduledRunUtc = null;
    policy.BackupDirectory = request.BackupDirectory.Trim();
    policy.ScheduleCron = normalizedCron;
    policy.MaxLocalBackups = Math.Clamp(request.MaxLocalBackups, 1, 1000);
    policy.VerifyAfterBackup = request.VerifyAfterBackup;
    policy.IsEnabled = request.IsEnabled;
    await db.SaveChangesAsync(ct);
    return Results.Ok(policy);
});

app.MapDelete("/api/databases/{id:guid}", async (Guid id, bool deleteHistory, bool deleteFiles, OdinVaultDbContext db, CancellationToken ct) =>
{
    var endpoint = await db.DatabaseEndpoints.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (endpoint is null)
        return Results.NotFound(new { message = "Database endpoint was not found." });
    if (await db.BackupRecords.AnyAsync(x => x.DatabaseEndpointId == id && x.Status == BackupStatus.Running, ct))
        return Results.Conflict(new { message = "A backup is currently running for this database." });

    var records = await db.BackupRecords.Where(x => x.DatabaseEndpointId == id).ToListAsync(ct);
    if (records.Count > 0 && !deleteHistory)
        return Results.Conflict(new { message = "Backup history exists. Retry with deleteHistory=true if you want to remove this database configuration and its history." });

    if (deleteFiles)
    {
        foreach (var record in records)
        {
            if (!string.IsNullOrWhiteSpace(record.FilePath) && File.Exists(record.FilePath))
            {
                try { File.Delete(record.FilePath); }
                catch (Exception ex) { app.Logger.LogWarning(ex, "Could not delete backup file {FilePath}.", record.FilePath); }
            }
        }
    }

    var links = await db.DatabaseStorageTargets.Where(x => x.DatabaseEndpointId == id).ToListAsync(ct);
    db.DatabaseStorageTargets.RemoveRange(links);
    var policy = await db.BackupPolicies.FirstOrDefaultAsync(x => x.DatabaseEndpointId == id, ct);
    if (policy is not null)
        db.BackupPolicies.Remove(policy);
    if (deleteHistory)
    {
        var recordIds = records.Select(x => x.Id).ToList();
        var replicas = await db.BackupReplicas.Where(x => recordIds.Contains(x.BackupRecordId)).ToListAsync(ct);
        db.BackupReplicas.RemoveRange(replicas);
        db.BackupRecords.RemoveRange(records);
    }
    db.DatabaseEndpoints.Remove(endpoint);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

app.MapPost("/api/databases/{id:guid}/test", async (Guid id, BackupOrchestrator orchestrator, CancellationToken ct) =>
{
    try { await orchestrator.TestConnectionAsync(id, ct); return Results.Ok(new { success = true }); }
    catch (KeyNotFoundException ex) { return Results.NotFound(new { success = false, message = ex.Message }); }
    catch (Exception ex) { return Results.BadRequest(new { success = false, message = ex.Message }); }
});

app.MapPost("/api/databases/{id:guid}/backups", async (Guid id, BackupOrchestrator orchestrator, CancellationToken ct) =>
{
    try { return Results.Ok(await orchestrator.RunNowAsync(id, ct)); }
    catch (KeyNotFoundException ex) { return Results.NotFound(new { message = ex.Message }); }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

app.MapGet("/api/databases/{id:guid}/backups", async (Guid id, int? take, OdinVaultDbContext db, CancellationToken ct) =>
{
    if (!await db.DatabaseEndpoints.AnyAsync(x => x.Id == id, ct))
        return Results.NotFound(new { message = "Database endpoint was not found." });
    var limit = Math.Clamp(take ?? 100, 1, 500);
    var records = await db.BackupRecords.Where(x => x.DatabaseEndpointId == id).OrderByDescending(x => x.StartedAtUtc).Take(limit).ToListAsync(ct);
    return Results.Ok(records);
});

app.MapGet("/api/backups/{id:guid}/download", async (Guid id, OdinVaultDbContext db, CancellationToken ct) =>
{
    var record = await db.BackupRecords.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (record is null)
        return Results.NotFound(new { message = "Backup was not found." });
    if (record.Status != BackupStatus.Succeeded || string.IsNullOrWhiteSpace(record.FilePath) || !File.Exists(record.FilePath))
        return Results.NotFound(new { message = "Backup file is not available on this agent." });
    return Results.File(record.FilePath, "application/octet-stream", record.FileName, enableRangeProcessing: true);
});

app.MapBackupJobs();
app.MapStorageEndpoints();
app.MapReplicaTargetEndpoints();
app.MapReplicaEndpoints(app.Services.GetRequiredService<ReplicaSettings>());
app.Run();

static object ToDatabaseResponse(DatabaseEndpoint endpoint, BackupPolicy? policy) => new
{
    endpoint.Id,
    endpoint.Name,
    endpoint.Engine,
    endpoint.Host,
    endpoint.Port,
    endpoint.DatabaseName,
    endpoint.Username,
    hasPassword = !string.IsNullOrWhiteSpace(endpoint.ProtectedPassword),
    endpoint.TrustServerCertificate,
    endpoint.IsEnabled,
    endpoint.CreatedAtUtc,
    policy = policy is null ? null : new
    {
        policy.BackupDirectory,
        policy.ScheduleCron,
        policy.MaxLocalBackups,
        policy.VerifyAfterBackup,
        policy.IsEnabled,
        policy.LastScheduledRunUtc
    }
};

static async Task<bool> HasDatabaseIdentityConflictAsync(
    OdinVaultDbContext db,
    Guid? excludedId,
    DatabaseEngine engine,
    string host,
    int? port,
    string databaseName,
    CancellationToken cancellationToken)
{
    var requestedIdentity = DatabaseIdentity.Create(engine, host, port, databaseName);
    var endpoints = await db.DatabaseEndpoints
        .AsNoTracking()
        .Where(x => x.Engine == engine && (!excludedId.HasValue || x.Id != excludedId.Value))
        .Select(x => new { x.Engine, x.Host, x.Port, x.DatabaseName })
        .ToListAsync(cancellationToken);

    return endpoints.Any(x =>
        string.Equals(
            DatabaseIdentity.Create(x.Engine, x.Host, x.Port, x.DatabaseName),
            requestedIdentity,
            StringComparison.Ordinal));
}

static bool IsDatabaseIdentityConflict(DbUpdateException exception) =>
    exception.InnerException is Microsoft.Data.Sqlite.SqliteException sqlite &&
    sqlite.SqliteErrorCode == 19 &&
    sqlite.Message.Contains("database_endpoint_identity_conflict", StringComparison.OrdinalIgnoreCase);

static string? ValidateDatabaseRequest(string name, string host, string databaseName, string backupDirectory, string? cron)
{
    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(databaseName) || string.IsNullOrWhiteSpace(backupDirectory))
        return "Name, host, databaseName and backupDirectory are required.";
    return ValidateCron(cron);
}

static string? ValidateCron(string? cron)
{
    if (string.IsNullOrWhiteSpace(cron))
        return null;
    try { CronExpression.Parse(cron.Trim(), CronFormat.Standard); return null; }
    catch (CronFormatException ex) { return $"Invalid cron expression: {ex.Message}"; }
}

static string? NormalizeCron(string? cron) => string.IsNullOrWhiteSpace(cron) ? null : cron.Trim();

public sealed record DiscoverSqlServerRequest(string Host, int? Port, string? Username, string? Password, bool TrustServerCertificate = true);
public sealed record CreateDatabaseRequest(string Name, string Host, int? Port, string DatabaseName, string? Username, string? Password, bool TrustServerCertificate, string BackupDirectory, int MaxLocalBackups = 7, bool VerifyAfterBackup = true, string? ScheduleCron = null, bool IsEnabled = true);
public sealed record UpdateDatabaseRequest(string Name, string Host, int? Port, string DatabaseName, string? Username, string? Password, bool ClearPassword, bool TrustServerCertificate, bool IsEnabled);
public sealed record UpdateBackupPolicyRequest(string BackupDirectory, int MaxLocalBackups, bool VerifyAfterBackup, string? ScheduleCron, bool IsEnabled);

public partial class Program { }
