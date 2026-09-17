using System.Security.Cryptography;
using System.Text;

namespace OdinVault.Agent;

public sealed class AgentApiKey(string value)
{
    public string Value { get; } = value;
}

public static class AgentApiKeyFactory
{
    public static AgentApiKey LoadOrCreate(string dataDirectory, string? configuredKey)
    {
        if (!string.IsNullOrWhiteSpace(configuredKey))
            return new AgentApiKey(configuredKey.Trim());

        var keyPath = Path.Combine(dataDirectory, "agent-api-key.txt");
        if (File.Exists(keyPath))
        {
            var existing = File.ReadAllText(keyPath).Trim();
            if (!string.IsNullOrWhiteSpace(existing))
                return new AgentApiKey(existing);
        }

        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(keyPath, key, Encoding.UTF8);
        return new AgentApiKey(key);
    }
}

public sealed class AgentApiKeyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AgentApiKey apiKey)
    {
        if (context.Request.Path.StartsWithSegments("/api/health") ||
            context.Request.Path.StartsWithSegments("/openapi"))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-OdinVault-Key", out var provided))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Missing X-OdinVault-Key header." });
            return;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(apiKey.Value);
        var providedBytes = Encoding.UTF8.GetBytes(provided.ToString());
        if (expectedBytes.Length != providedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Invalid OdinVault API key." });
            return;
        }

        await next(context);
    }
}
