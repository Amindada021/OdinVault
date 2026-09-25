namespace OdinVault.Agent;

public sealed class RequestCorrelationMiddleware(
    RequestDelegate next,
    ILogger<RequestCorrelationMiddleware> logger)
{
    private const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = IsSafe(supplied)
            ? supplied!
            : Guid.NewGuid().ToString("N");

        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await next(context);
        }
    }

    private static bool IsSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
            return false;

        return value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');
    }
}
