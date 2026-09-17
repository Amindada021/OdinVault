using Microsoft.EntityFrameworkCore;

namespace OdinVault.Persistence;

public static class SqliteMigrationBootstrapper
{
    private const string BaselineMigrationId = "20260917020000_Baseline";
    private const string ProductVersion = "10.0.12";

    public static async Task PrepareAsync(OdinVaultDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = db.Database.GetDbConnection();

            await using var tableCommand = connection.CreateCommand();
            tableCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DatabaseEndpoints';";
            var hasLegacySchema = Convert.ToInt32(await tableCommand.ExecuteScalarAsync(cancellationToken)) > 0;

            await using var historyCommand = connection.CreateCommand();
            historyCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory';";
            var hasHistory = Convert.ToInt32(await historyCommand.ExecuteScalarAsync(cancellationToken)) > 0;

            if (!hasLegacySchema || hasHistory)
                return;

            await using var createHistory = connection.CreateCommand();
            createHistory.CommandText = """
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);
""";
            await createHistory.ExecuteNonQueryAsync(cancellationToken);

            await using var insertBaseline = connection.CreateCommand();
            insertBaseline.CommandText = "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ($id, $version);";
            var id = insertBaseline.CreateParameter();
            id.ParameterName = "$id";
            id.Value = BaselineMigrationId;
            insertBaseline.Parameters.Add(id);
            var version = insertBaseline.CreateParameter();
            version.ParameterName = "$version";
            version.Value = ProductVersion;
            insertBaseline.Parameters.Add(version);
            await insertBaseline.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
