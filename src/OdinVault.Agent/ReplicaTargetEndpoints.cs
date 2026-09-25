using Microsoft.EntityFrameworkCore;
using OdinVault.Core;
using OdinVault.Persistence;

namespace OdinVault.Agent;

public static class ReplicaTargetEndpoints
{
    public static void MapReplicaTargetEndpoints(this WebApplication app)
    {
        app.MapPost("/api/storage-targets/odinvault-replica", async (
            CreateReplicaTargetRequest request,
            OdinVaultDbContext db,
            ISecretProtector protector,
            IHttpClientFactory httpClientFactory,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.BaseUrl) || string.IsNullOrWhiteSpace(request.ApiKey))
                return Results.BadRequest(new { message = "name, baseUrl and apiKey are required." });

            if (!Uri.TryCreate(request.BaseUrl.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return Results.BadRequest(new { message = "baseUrl must be an absolute HTTP or HTTPS URL." });

            var target = new StorageTarget
            {
                Name = request.Name.Trim(),
                Type = StorageProviderType.OdinVaultReplica,
                BaseUrl = request.BaseUrl.Trim().TrimEnd('/'),
                ProtectedApiKey = protector.Protect(request.ApiKey),
                IsEnabled = request.IsEnabled
            };

            db.StorageTargets.Add(target);
            await db.SaveChangesAsync(ct);

            if (request.TestConnection)
            {
                var client = httpClientFactory.CreateClient("OdinVaultReplica");
                using var health = new HttpRequestMessage(HttpMethod.Get, $"{target.BaseUrl}/api/databases");
                health.Headers.TryAddWithoutValidation("X-OdinVault-Key", request.ApiKey);
                using var response = await client.SendAsync(health, ct);
                if (!response.IsSuccessStatusCode)
                    return Results.Created($"/api/storage-targets/{target.Id}", new { target.Id, target.Name, target.Type, target.BaseUrl, target.IsEnabled, testSucceeded = false });
            }

            return Results.Created($"/api/storage-targets/{target.Id}", new { target.Id, target.Name, target.Type, target.BaseUrl, target.IsEnabled, testSucceeded = request.TestConnection ? true : (bool?)null });
        });

        app.MapPost("/api/storage-targets/{id:guid}/test", async (
            Guid id,
            OdinVaultDbContext db,
            ISecretProtector protector,
            IHttpClientFactory httpClientFactory,
            CancellationToken ct) =>
        {
            var target = await db.StorageTargets.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (target is null) return Results.NotFound(new { message = "Storage target was not found." });
            if (target.Type != StorageProviderType.OdinVaultReplica) return Results.BadRequest(new { message = "This target is not an OdinVault replica." });
            if (string.IsNullOrWhiteSpace(target.BaseUrl) || string.IsNullOrWhiteSpace(target.ProtectedApiKey)) return Results.BadRequest(new { message = "Replica target is incomplete." });

            var apiKey = protector.Unprotect(target.ProtectedApiKey);
            var client = httpClientFactory.CreateClient("OdinVaultReplica");
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{target.BaseUrl.TrimEnd('/')}/api/databases");
            request.Headers.TryAddWithoutValidation("X-OdinVault-Key", apiKey);
            using var response = await client.SendAsync(request, ct);
            return Results.Ok(new { success = response.IsSuccessStatusCode, statusCode = (int)response.StatusCode });
        });
    }
}

public sealed record CreateReplicaTargetRequest(
    string Name,
    string BaseUrl,
    string ApiKey,
    bool IsEnabled = true,
    bool TestConnection = true);
