using Microsoft.Data.SqlClient;
using OdinVault.Core;

namespace OdinVault.Database.SqlServer;

public sealed record SqlServerRestorePreflightResult(
    string TargetDatabaseName,
    string ProductVersion,
    string DataDirectory,
    string LogDirectory,
    IReadOnlyList<SqlServerRestoreFilePlan> Files);

public sealed record SqlServerRestoreFilePlan(
    string LogicalName,
    char Type,
    string TargetPath);

public sealed record SqlServerRestoreResult(
    string TargetDatabaseName,
    DateTime CompletedAtUtc,
    TimeSpan Duration);

public sealed class SqlServerRestoreService
{
    public async Task<SqlServerRestorePreflightResult> PreflightAsync(
        DatabaseConnectionInfo connectionInfo,
        string backupPath,
        string targetDatabaseName,
        CancellationToken cancellationToken = default)
    {
        ValidateTargetDatabaseName(targetDatabaseName);

        await using var connection = new SqlConnection(
            BuildConnectionString(connectionInfo, "master"));
        await connection.OpenAsync(cancellationToken);

        if (await DatabaseExistsAsync(connection, targetDatabaseName, cancellationToken))
            throw new InvalidOperationException("Target database already exists. Restore-to-new-DB never overwrites an existing database.");

        await using (var verify = new SqlCommand(
            "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;",
            connection)
        {
            CommandTimeout = 0
        })
        {
            verify.Parameters.AddWithValue("@path", backupPath);
            await verify.ExecuteNonQueryAsync(cancellationToken);
        }

        var serverInfo = await ReadServerPathsAsync(connection, cancellationToken);
        var files = await ReadBackupFilesAsync(
            connection,
            backupPath,
            targetDatabaseName,
            serverInfo.DataDirectory,
            serverInfo.LogDirectory,
            cancellationToken);

        if (files.Count == 0)
            throw new InvalidOperationException("Backup does not contain any restorable database files.");

        return new SqlServerRestorePreflightResult(
            targetDatabaseName,
            serverInfo.ProductVersion,
            serverInfo.DataDirectory,
            serverInfo.LogDirectory,
            files);
    }

    public async Task<SqlServerRestoreResult> RestoreToNewDatabaseAsync(
        DatabaseConnectionInfo connectionInfo,
        string backupPath,
        string targetDatabaseName,
        CancellationToken cancellationToken = default)
    {
        var preflight = await PreflightAsync(
            connectionInfo,
            backupPath,
            targetDatabaseName,
            cancellationToken);

        await using var connection = new SqlConnection(
            BuildConnectionString(connectionInfo, "master"));
        await connection.OpenAsync(cancellationToken);

        var started = DateTime.UtcNow;
        var quotedDatabase = new SqlCommandBuilder().QuoteIdentifier(targetDatabaseName);

        var moveClauses = preflight.Files
            .Select((file, index) => $"MOVE @logical{index} TO @target{index}")
            .ToArray();

        var sql =
            $"RESTORE DATABASE {quotedDatabase} FROM DISK = @path WITH " +
            string.Join(", ", moveClauses) +
            ", RECOVERY, STATS = 5;";

        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 0
        };
        command.Parameters.AddWithValue("@path", backupPath);

        for (var i = 0; i < preflight.Files.Count; i++)
        {
            command.Parameters.AddWithValue($"@logical{i}", preflight.Files[i].LogicalName);
            command.Parameters.AddWithValue($"@target{i}", preflight.Files[i].TargetPath);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);

        if (!await DatabaseExistsAsync(connection, targetDatabaseName, cancellationToken))
            throw new InvalidOperationException("SQL Server did not report the restored database after RESTORE completed.");

        var completed = DateTime.UtcNow;
        return new SqlServerRestoreResult(
            targetDatabaseName,
            completed,
            completed - started);
    }

    private static async Task<bool> DatabaseExistsAsync(
        SqlConnection connection,
        string databaseName,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT CASE WHEN DB_ID(@databaseName) IS NULL THEN 0 ELSE 1 END;",
            connection);
        command.Parameters.AddWithValue("@databaseName", databaseName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<(string ProductVersion, string DataDirectory, string LogDirectory)> ReadServerPathsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT
    CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')),
    CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultDataPath')),
    CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultLogPath')),
    (SELECT TOP (1) physical_name FROM master.sys.database_files WHERE type = 0 ORDER BY file_id),
    (SELECT TOP (1) physical_name FROM master.sys.database_files WHERE type = 1 ORDER BY file_id);
""";

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Could not read SQL Server default data paths.");

        var version = reader.IsDBNull(0) ? "unknown" : reader.GetString(0);
        var dataDirectory = reader.IsDBNull(1) ? null : reader.GetString(1);
        var logDirectory = reader.IsDBNull(2) ? null : reader.GetString(2);

        if (string.IsNullOrWhiteSpace(dataDirectory) && !reader.IsDBNull(3))
            dataDirectory = Path.GetDirectoryName(reader.GetString(3));
        if (string.IsNullOrWhiteSpace(logDirectory) && !reader.IsDBNull(4))
            logDirectory = Path.GetDirectoryName(reader.GetString(4));

        if (string.IsNullOrWhiteSpace(dataDirectory) || string.IsNullOrWhiteSpace(logDirectory))
            throw new InvalidOperationException("SQL Server default data or log directory could not be determined.");

        return (
            version,
            dataDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            logDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static async Task<List<SqlServerRestoreFilePlan>> ReadBackupFilesAsync(
        SqlConnection connection,
        string backupPath,
        string targetDatabaseName,
        string dataDirectory,
        string logDirectory,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "RESTORE FILELISTONLY FROM DISK = @path;",
            connection)
        {
            CommandTimeout = 0
        };
        command.Parameters.AddWithValue("@path", backupPath);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var logicalOrdinal = reader.GetOrdinal("LogicalName");
        var typeOrdinal = reader.GetOrdinal("Type");

        var result = new List<SqlServerRestoreFilePlan>();
        var dataIndex = 0;
        var logIndex = 0;
        var safeName = SanitizeFileName(targetDatabaseName);

        while (await reader.ReadAsync(cancellationToken))
        {
            var logicalName = reader.GetString(logicalOrdinal);
            var typeText = reader.GetString(typeOrdinal);
            var type = string.IsNullOrWhiteSpace(typeText) ? 'D' : typeText[0];

            string targetPath;
            if (type == 'L')
            {
                logIndex++;
                var suffix = logIndex == 1 ? "_log.ldf" : $"_log_{logIndex}.ldf";
                targetPath = Path.Combine(logDirectory, safeName + suffix);
            }
            else
            {
                dataIndex++;
                var extension = dataIndex == 1 ? ".mdf" : $"-{dataIndex}.ndf";
                targetPath = Path.Combine(dataDirectory, safeName + extension);
            }

            result.Add(new SqlServerRestoreFilePlan(logicalName, type, targetPath));
        }

        return result;
    }

    private static void ValidateTargetDatabaseName(string databaseName)
    {
        var value = databaseName?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Target database name is required.", nameof(databaseName));
        if (value.Length > 128)
            throw new ArgumentException("Target database name cannot exceed 128 characters.", nameof(databaseName));
        if (value.Any(char.IsControl))
            throw new ArgumentException("Target database name contains invalid characters.", nameof(databaseName));
    }

    private static string BuildConnectionString(
        DatabaseConnectionInfo connection,
        string initialCatalog)
    {
        var dataSource = connection.Port is > 0
            ? $"{connection.Host},{connection.Port}"
            : connection.Host;

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = initialCatalog,
            TrustServerCertificate = connection.TrustServerCertificate,
            Encrypt = true,
            ConnectTimeout = 15,
            ApplicationName = "OdinVault Restore"
        };

        if (string.IsNullOrWhiteSpace(connection.Username))
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = connection.Username;
            builder.Password = connection.Password ?? string.Empty;
        }

        return builder.ConnectionString;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }
}
