namespace OdinVault.Agent;

public static class RestoreEndpoints
{
    public static void MapRestoreEndpoints(this WebApplication app)
    {
        app.MapPost("/api/restores/preflight", async (
            RestoreRequest request,
            RestoreOrchestrator restore,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await restore.PreflightAsync(
                    request.BackupId,
                    request.TargetDatabaseName,
                    ct));
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

        app.MapPost("/api/restores", async (
            RestoreRequest request,
            RestoreOrchestrator restore,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await restore.RestoreAsync(
                    request.BackupId,
                    request.TargetDatabaseName,
                    ct));
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
    }
}

public sealed record RestoreRequest(
    Guid BackupId,
    string TargetDatabaseName);
