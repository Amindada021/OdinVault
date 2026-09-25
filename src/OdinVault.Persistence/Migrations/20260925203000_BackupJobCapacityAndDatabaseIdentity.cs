using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace OdinVault.Persistence.Migrations;

[DbContext(typeof(OdinVaultDbContext))]
[Migration("20260925203000_BackupJobCapacityAndDatabaseIdentity")]
public sealed class BackupJobCapacityAndDatabaseIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
CREATE TRIGGER IF NOT EXISTS "TR_BackupJobs_QueueCapacity"
BEFORE INSERT ON "BackupJobs"
WHEN NEW."Status" = 0
 AND (SELECT COUNT(*) FROM "BackupJobs" WHERE "Status" = 0) >= 64
BEGIN
    SELECT RAISE(ABORT, 'backup_job_queue_full');
END;

CREATE TRIGGER IF NOT EXISTS "TR_DatabaseEndpoints_UniqueIdentity_Insert"
BEFORE INSERT ON "DatabaseEndpoints"
WHEN EXISTS (
    SELECT 1
    FROM "DatabaseEndpoints" d
    WHERE d."Engine" = NEW."Engine"
      AND upper(rtrim(trim(d."Host"), '.')) = upper(rtrim(trim(NEW."Host"), '.'))
      AND ifnull(d."Port", -1) = ifnull(NEW."Port", -1)
      AND upper(trim(d."DatabaseName")) = upper(trim(NEW."DatabaseName"))
)
BEGIN
    SELECT RAISE(ABORT, 'database_endpoint_identity_conflict');
END;

CREATE TRIGGER IF NOT EXISTS "TR_DatabaseEndpoints_UniqueIdentity_Update"
BEFORE UPDATE OF "Engine", "Host", "Port", "DatabaseName" ON "DatabaseEndpoints"
WHEN EXISTS (
    SELECT 1
    FROM "DatabaseEndpoints" d
    WHERE d."Id" <> NEW."Id"
      AND d."Engine" = NEW."Engine"
      AND upper(rtrim(trim(d."Host"), '.')) = upper(rtrim(trim(NEW."Host"), '.'))
      AND ifnull(d."Port", -1) = ifnull(NEW."Port", -1)
      AND upper(trim(d."DatabaseName")) = upper(trim(NEW."DatabaseName"))
)
BEGIN
    SELECT RAISE(ABORT, 'database_endpoint_identity_conflict');
END;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DROP TRIGGER IF EXISTS "TR_DatabaseEndpoints_UniqueIdentity_Update";
DROP TRIGGER IF EXISTS "TR_DatabaseEndpoints_UniqueIdentity_Insert";
DROP TRIGGER IF EXISTS "TR_BackupJobs_QueueCapacity";
""");
    }
}
