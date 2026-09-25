using Microsoft.Data.SqlClient;
using OdinVault.Core;

namespace OdinVault.Database.SqlServer;

public sealed class SqlServerBackupProvider : IDatabaseBackupProvider
{
    public DatabaseEngine Engine => DatabaseEngine.SqlServer;

    public async Task TestConnectionAsync(DatabaseConnectionInfo connection, CancellationToken cancellationToken = default)
    {
        await using var sqlConnection = new SqlConnection(BuildConnectionString(connection, connection.DatabaseName));
        await sqlConnection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT 1", sqlConnection);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task<BackupExecutionResult> CreateBackupAsync(
        BackupExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var databaseName = request.Connection.DatabaseName;
        var fileName = BackupNaming.Create(databaseName);
        var backupPath = Path.Combine(request.BackupDirectory, fileName);

        await using var connection = new SqlConnection(BuildConnectionString(request.Connection, "master"));
        await connection.OpenAsync(cancellationToken);
        connection.InfoMessage += (_, args) =>
        {
            foreach (SqlError error in args.Errors)
            {
                var match = System.Text.RegularExpressions.Regex.Match(error.Message, @"(\d+)\s*(?:percent|%)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
                    request.Progress?.Report(new BackupProgress("backup", Math.Clamp(percent, 0, 100)));
            }
        };
        request.Progress?.Report(new BackupProgress("backup", 0));

        var quotedDatabase = new SqlCommandBuilder().QuoteIdentifier(databaseName);
        var backupSql = $"BACKUP DATABASE {quotedDatabase} TO DISK = @path WITH COPY_ONLY, INIT, COMPRESSION, CHECKSUM, STATS = 5;";

        await using (var backupCommand = new SqlCommand(backupSql, connection) { CommandTimeout = 0 })
        {
            backupCommand.Parameters.AddWithValue("@path", backupPath);
            await backupCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var verificationStatus = VerificationStatus.NotRequested;
        if (request.VerifyAfterBackup)
        {
            verificationStatus = VerificationStatus.Pending;
            request.Progress?.Report(new BackupProgress("verify"));
            await using var verifyCommand = new SqlCommand(
                "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;",
                connection)
            {
                CommandTimeout = 0
            };
            verifyCommand.Parameters.AddWithValue("@path", backupPath);
            await verifyCommand.ExecuteNonQueryAsync(cancellationToken);
            verificationStatus = VerificationStatus.Verified;
        }

        var sizeBytes = File.Exists(backupPath) ? new FileInfo(backupPath).Length : await ReadBackupSizeAsync(connection, databaseName, backupPath, cancellationToken);

        return new BackupExecutionResult(fileName, backupPath, sizeBytes, verificationStatus);
    }

    private static async Task<long> ReadBackupSizeAsync(
        SqlConnection connection,
        string databaseName,
        string backupPath,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) COALESCE(bs.compressed_backup_size, bs.backup_size, 0)
            FROM msdb.dbo.backupset bs
            INNER JOIN msdb.dbo.backupmediafamily bmf ON bmf.media_set_id = bs.media_set_id
            WHERE bs.database_name = @databaseName
              AND bmf.physical_device_name = @backupPath
            ORDER BY bs.backup_finish_date DESC;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@databaseName", databaseName);
        command.Parameters.AddWithValue("@backupPath", backupPath);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    private static string BuildConnectionString(DatabaseConnectionInfo connection, string initialCatalog)
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
            ApplicationName = "OdinVault"
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
