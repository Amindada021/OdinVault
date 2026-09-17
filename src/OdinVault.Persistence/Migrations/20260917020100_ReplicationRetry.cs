using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace OdinVault.Persistence.Migrations;

[DbContext(typeof(OdinVaultDbContext))]
[Migration("20260917020100_ReplicationRetry")]
public sealed class ReplicationRetry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "RetryCount",
            table: "BackupReplicas",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "NextRetryAtUtc",
            table: "BackupReplicas",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_BackupReplicas_Status_NextRetryAtUtc",
            table: "BackupReplicas",
            columns: new[] { "Status", "NextRetryAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BackupReplicas_Status_NextRetryAtUtc",
            table: "BackupReplicas");

        migrationBuilder.DropColumn(name: "RetryCount", table: "BackupReplicas");
        migrationBuilder.DropColumn(name: "NextRetryAtUtc", table: "BackupReplicas");
    }
}
