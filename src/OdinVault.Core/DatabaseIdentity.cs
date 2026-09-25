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
        var normalizedHost = (host ?? string.Empty).Trim().TrimEnd('.');
        var normalizedPort = port;

        if (engine == DatabaseEngine.SqlServer)
        {
            (normalizedHost, normalizedPort) = NormalizeSqlServerAddress(
                normalizedHost,
                normalizedPort);
        }

        var normalizedDatabase = (databaseName ?? string.Empty).Trim().ToUpperInvariant();
        return $"{(int)engine}|{normalizedHost.ToUpperInvariant()}|{normalizedPort?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty}|{normalizedDatabase}";
    }

    public static (string Host, int? Port) NormalizeSqlServerAddress(string host, int? port)
    {
        var normalizedHost = (host ?? string.Empty).Trim().TrimEnd('.');
        var normalizedPort = port;

        if (normalizedPort is null)
        {
            var comma = normalizedHost.LastIndexOf(',');
            if (comma > 0 &&
                comma < normalizedHost.Length - 1 &&
                int.TryParse(
                    normalizedHost[(comma + 1)..].Trim(),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsedPort) &&
                parsedPort is > 0 and <= 65535)
            {
                normalizedHost = normalizedHost[..comma].Trim().TrimEnd('.');
                normalizedPort = parsedPort;
            }
        }

        if (normalizedPort is null && !normalizedHost.Contains('\\'))
            normalizedPort = 1433;

        return (normalizedHost, normalizedPort);
    }
}
