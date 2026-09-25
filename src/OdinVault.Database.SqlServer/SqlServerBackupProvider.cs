using System.Net;
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

    public async Task<BackupPreflightResult> PreflightAsync(
        BackupExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BackupDirectory))
            throw new InvalidOperationException("Backup directory is not configured.");

        if (!IsLocalSqlServer(request.Connection.Host) && !IsUncPath(request.BackupDirectory))
        {
            throw new InvalidOperationException(
                "Remote SQL Server backups must use a UNC/shared backup directory that is accessible to both the SQL Server service and the OdinVault Agent.");
        }

        if (!Directory.Exists(request.BackupDirectory))
            throw new InvalidOperationException("Backup directory does not exist or is not accessible by the OdinVault Agent. Remote SQL Server backups require a shared UNC path visible to both services.");

        var probePath = Path.Combine(request.BackupDirectory, $".odinvault-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var probe = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                useAsync: true))
            {
                await probe.FlushAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(
                "Backup directory is not writable by the OdinVault Agent service account.",
                ex);
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                    File.Delete(probePath);
            }
            catch
            {
                // Cleanup failure must not hide the actual preflight result.
            }
        }

        await using var connection = new SqlConnection(BuildConnectionString(request.Connection, "master"));
        await connection.OpenAsync(cancellationToken);

        const string sql = """
SELECT
    CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS ProductVersion,
    CONVERT(nvarchar(256), SERVERPROPERTY('Edition')) AS Edition,
    d.state_desc,
    HAS_PERMS_BY_NAME(d.name, 'DATABASE', 'BACKUP DATABASE') AS CanBackup,
    (
        SELECT SUM(CONVERT(bigint, mf.size)) * 8192
        FROM sys.master_files mf
        WHERE mf.database_id = d.database_id
    ) AS DatabaseSizeBytes
FROM sys.databases d
WHERE d.name = @databaseName;
""";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@databaseName", request.Connection.DatabaseName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Database was not found on the SQL Server.");

        var productVersion = reader.IsDBNull(0) ? "unknown" : reader.GetString(0);
        var edition = reader.IsDBNull(1) ? "unknown" : reader.GetString(1);
        var state = reader.IsDBNull(2) ? "UNKNOWN" : reader.GetString(2);
        var canBackup = !reader.IsDBNull(3) && Convert.ToInt32(reader.GetValue(3)) == 1;
        var databaseSizeBytes = reader.IsDBNull(4) ? (long?)null : Convert.ToInt64(reader.GetValue(4));

        if (!string.Equals(state, "ONLINE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Database is not ONLINE. Current state: {state}.");

        if (!canBackup)
            throw new InvalidOperationException("The SQL credential does not have BACKUP DATABASE permission.");

        long? destinationFreeBytes = null;
        var warnings = new List<string>();

        try
        {
            var fullPath = Path.GetFullPath(request.BackupDirectory);
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrWhiteSpace(root))
                destinationFreeBytes = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            warnings.Add("Destination free space could not be measured by the Agent.");
        }

        const long minimumFreeBytes = 256L * 1024 * 1024;
        if (destinationFreeBytes is < minimumFreeBytes)
            throw new InvalidOperationException("Backup destination has less than 256 MB free space.");

        if (databaseSizeBytes is > 0 &&
            destinationFreeBytes is > 0 &&
            destinationFreeBytes < databaseSizeBytes)
        {
            warnings.Add("Destination free space is smaller than the database's allocated size; compression may still allow the backup to succeed.");
        }

        warnings.Add("Agent access to the backup path was verified. SQL Server service-account write access is finally proven by the BACKUP DATABASE command itself.");

        return new BackupPreflightResult(
            productVersion,
            edition,
            databaseSizeBytes,
            destinationFreeBytes,
            warnings);
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
        string? verificationError = null;
        if (request.VerifyAfterBackup)
        {
            verificationStatus = VerificationStatus.Pending;
            request.Progress?.Report(new BackupProgress("verify"));
            try
            {
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                verificationStatus = VerificationStatus.Failed;
                verificationError = ex.Message;
            }
        }

        var sizeBytes = File.Exists(backupPath) ? new FileInfo(backupPath).Length : await ReadBackupSizeAsync(connection, databaseName, backupPath, cancellationToken);

        return new BackupExecutionResult(fileName, backupPath, sizeBytes, verificationStatus, verificationError);
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

    private static bool IsUncPath(string path) =>
        OperatingSystem.IsWindows() &&
        path.TrimStart().StartsWith(@"\\", StringComparison.Ordinal);

    private static bool IsLocalSqlServer(string host)
    {
        var normalized = (host ?? string.Empty).Trim();
        var comma = normalized.LastIndexOf(',');
        if (comma > 0)
            normalized = normalized[..comma];

        var slash = normalized.IndexOf('\\');
        if (slash > 0)
            normalized = normalized[..slash];

        normalized = normalized.Trim().TrimEnd('.');
        if (normalized is "." or "(local)" ||
            string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var machine = Environment.MachineName.Trim().TrimEnd('.');
        var dnsHost = Dns.GetHostName().Trim().TrimEnd('.');
        return string.Equals(normalized, machine, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, dnsHost, StringComparison.OrdinalIgnoreCase);
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
