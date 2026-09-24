using Microsoft.Data.SqlClient;

namespace OdinVault.Database.SqlServer;

public sealed class SqlServerDiscoveryService
{
    public async Task<IReadOnlyList<SqlServerDatabaseInfo>> DiscoverAsync(
        string host,
        int? port,
        string? username,
        string? password,
        bool trustServerCertificate,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("SQL Server host is required.", nameof(host));

        var dataSource = port is > 0 ? $"{host.Trim()},{port}" : host.Trim();
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = "master",
            TrustServerCertificate = trustServerCertificate,
            Encrypt = true,
            ConnectTimeout = 15,
            ApplicationName = "OdinVault"
        };

        if (string.IsNullOrWhiteSpace(username))
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = username.Trim();
            builder.Password = password ?? string.Empty;
        }

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                d.name,
                d.database_id,
                d.state_desc,
                d.recovery_model_desc,
                CAST(CASE WHEN d.database_id <= 4 THEN 1 ELSE 0 END AS bit) AS is_system
            FROM sys.databases d
            WHERE d.source_database_id IS NULL
            ORDER BY
                CASE WHEN d.database_id <= 4 THEN 1 ELSE 0 END,
                d.name;
            """;

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new List<SqlServerDatabaseInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SqlServerDatabaseInfo(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4)));
        }

        return result;
    }
}

public sealed record SqlServerDatabaseInfo(
    string Name,
    int DatabaseId,
    string State,
    string RecoveryModel,
    bool IsSystem);
