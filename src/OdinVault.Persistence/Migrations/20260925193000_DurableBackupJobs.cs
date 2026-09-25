using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace OdinVault.Persistence.Migrations;

[DbContext(typeof(OdinVaultDbContext))]
[Migration("20260925193000_DurableBackupJobs")]
public sealed class DurableBackupJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BackupJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                DatabaseEndpointId = table.Column<Guid>(type: "TEXT", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                Stage = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Percent = table.Column<int>(type: "INTEGER", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                ExecutionToken = table.Column<Guid>(type: "TEXT", nullable: true),
                BackupRecordId = table.Column<Guid>(type: "TEXT", nullable: true),
                ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BackupJobs", x => x.Id);
                table.CheckConstraint(
                    "CK_BackupJobs_Percent",
                    ""Percent" IS NULL OR ("Percent" >= 0 AND "Percent" <= 100)");
                table.ForeignKey(
                    name: "FK_BackupJobs_BackupRecords_BackupRecordId",
                    column: x => x.BackupRecordId,
                    principalTable: "BackupRecords",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BackupJobs_DatabaseEndpoints_DatabaseEndpointId",
                    column: x => x.DatabaseEndpointId,
                    principalTable: "DatabaseEndpoints",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BackupJobs_BackupRecordId",
            table: "BackupJobs",
            column: "BackupRecordId");

        migrationBuilder.CreateIndex(
            name: "IX_BackupJobs_RequestId",
            table: "BackupJobs",
            column: "RequestId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BackupJobs_Status_CreatedAtUtc_Id",
            table: "BackupJobs",
            columns: new[] { "Status", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_BackupJobs_DatabaseEndpointId",
            table: "BackupJobs",
            column: "DatabaseEndpointId",
            unique: true,
            filter: ""Status" IN (0, 1)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "BackupJobs");
    }
}
