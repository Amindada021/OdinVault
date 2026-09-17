using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OdinVault.Agent;
using OdinVault.Core;
using OdinVault.Database.SqlServer;
using OdinVault.Persistence;

var builder = WebApplication.CreateBuilder(args);

var dataDirectory = Path.GetFullPath(builder.Configuration["OdinVault:DataDirectory"] ?? "data");
Directory.CreateDirectory(dataDirectory);
Directory.CreateDirectory(Path.Combine(dataDirectory, "keys"));

builder.Services.AddOpenApi();
builder.Services.AddDbContext<OdinVaultDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "odinvault.db")}"));

builder.Services
    .AddDataProtection()
    .SetApplicationName("OdinVault")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));

builder.Services.AddScoped<ISecretProtector, SecretProtector>();
builder.Services.AddScoped<IDatabaseBackupProvider, SqlServerBackupProvider>();
builder.Services.AddScoped<BackupOrchestrator>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OdinVaultDbContext>();
    await db.Database.EnsureCreatedAsync();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/api/health", () => Results.Ok(new
{
    service = "OdinVault.Agent",
    status = "healthy",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/databases", async (OdinVaultDbContext db, CancellationToken ct) =>
{
    var items = await db.DatabaseEndpoints
        .OrderBy(x => x.Name)
        .Select(x => new
        {
            x.Id,
            x.Name,
            x.Engine,
            x.Host,
            x.Port,
            x.DatabaseName,
            x.Username,
            hasPassword = x.ProtectedPassword != null,
            x.TrustServerCertificate,
            x.IsEnabled
        })
        .ToListAsync(ct);
    return Results.Ok(items);
});

app.MapPost("/api/databases", async (
    CreateDatabaseRequest request,
    OdinVaultDbContext db,
    ISecretProtector protector,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) ||
        string.IsNullOrWhiteSpace(request.Host) ||
        string.IsNullOrWhiteSpace(request.DatabaseName) ||
        string.IsNullOrWhiteSpace(request.BackupDirectory))
        return Results.BadRequest(new { message = "Name, host, databaseName and backupDirectory are required." });

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
        IsEnabled = true
    };

    var policy = new BackupPolicy
    {
        DatabaseEndpointId = endpoint.Id,
        BackupDirectory = request.BackupDirectory.Trim(),
        MaxLocalBackups = Math.Clamp(request.MaxLocalBackups, 1, 1000),
        VerifyAfterBackup = request.VerifyAfterBackup,
        ScheduleCron = request.ScheduleCron,
        IsEnabled = true
    };

    db.DatabaseEndpoints.Add(endpoint);
    db.BackupPolicies.Add(policy);
    await db.SaveChangesAsync(ct);

    return Results.Created($"/api/databases/{endpoint.Id}", new { endpoint.Id, endpoint.Name });
});

app.MapPost("/api/databases/{id:guid}/test", async (Guid id, BackupOrchestrator orchestrator, CancellationToken ct) =>
{
    try
    {
        await orchestrator.TestConnectionAsync(id, ct);
        return Results.Ok(new { success = true });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, message = ex.Message });
    }
});

app.MapPost("/api/databases/{id:guid}/backups", async (Guid id, BackupOrchestrator orchestrator, CancellationToken ct) =>
{
    try
    {
        var record = await orchestrator.RunNowAsync(id, ct);
        return Results.Ok(record);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapGet("/api/databases/{id:guid}/backups", async (Guid id, OdinVaultDbContext db, CancellationToken ct) =>
{
    var records = await db.BackupRecords
        .Where(x => x.DatabaseEndpointId == id)
        .OrderByDescending(x => x.StartedAtUtc)
        .Take(200)
        .ToListAsync(ct);
    return Results.Ok(records);
});

app.MapGet("/api/backups/{id:guid}/download", async (Guid id, OdinVaultDbContext db, CancellationToken ct) =>
{
    var record = await db.BackupRecords.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (record is null) return Results.NotFound(new { message = "Backup was not found." });
    if (record.Status != BackupStatus.Succeeded || string.IsNullOrWhiteSpace(record.FilePath) || !File.Exists(record.FilePath))
        return Results.NotFound(new { message = "Backup file is not available on this agent." });

    return Results.File(record.FilePath, "application/octet-stream", record.FileName, enableRangeProcessing: true);
});

app.Run();

public sealed record CreateDatabaseRequest(
    string Name,
    string Host,
    int? Port,
    string DatabaseName,
    string? Username,
    string? Password,
    bool TrustServerCertificate,
    string BackupDirectory,
    int MaxLocalBackups = 7,
    bool VerifyAfterBackup = true,
    string? ScheduleCron = null);

public partial class Program;
