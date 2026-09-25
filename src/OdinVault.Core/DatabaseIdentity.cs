namespace OdinVault.Core;

public static class DatabaseIdentity
{
    public static string Create(DatabaseEndpoint endpoint) =>
        Create(endpoint.Engine, endpoint.Host, endpoint.Port, endpoint.DatabaseName);

    public static string Create(
        DatabaseEngine engine,
        string host,
        int? port,
        string databaseName)
    {
        var normalizedHost = (host ?? string.Empty).Trim().TrimEnd('.').ToUpperInvariant();
        var normalizedDatabase = (databaseName ?? string.Empty).Trim().ToUpperInvariant();
        return $"{(int)engine}|{normalizedHost}|{port?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty}|{normalizedDatabase}";
    }
}
