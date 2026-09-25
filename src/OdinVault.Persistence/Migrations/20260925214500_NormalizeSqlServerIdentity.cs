using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace OdinVault.Persistence.Migrations;

[DbContext(typeof(OdinVaultDbContext))]
[Migration("20260925214500_NormalizeSqlServerIdentity")]
public sealed class NormalizeSqlServerIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DROP TRIGGER IF EXISTS "TR_DatabaseEndpoints_UniqueIdentity_Update";
DROP TRIGGER IF EXISTS "TR_DatabaseEndpoints_UniqueIdentity_Insert";

CREATE TRIGGER "TR_DatabaseEndpoints_UniqueIdentity_Insert"
BEFORE INSERT ON "DatabaseEndpoints"
WHEN EXISTS (
    SELECT 1
    FROM "DatabaseEndpoints" d
    WHERE d."Engine" = NEW."Engine"
      AND upper(rtrim(trim(d."Host"), '.')) = upper(rtrim(trim(NEW."Host"), '.'))
      AND (
          CASE
              WHEN d."Engine" = 1 AND instr(d."Host", '') = 0 THEN ifnull(d."Port", 1433)
              ELSE ifnull(d."Port", -1)
          END
      ) = (
          CASE
              WHEN NEW."Engine" = 1 AND instr(NEW."Host", '') = 0 THEN ifnull(NEW."Port", 1433)
              ELSE ifnull(NEW."Port", -1)
          END
      )
      AND upper(trim(d."DatabaseName")) = upper(trim(NEW."DatabaseName"))
)
BEGIN
    SELECT RAISE(ABORT, 'database_endpoint_identity_conflict');
END;

CREATE TRIGGER "TR_DatabaseEndpoints_UniqueIdentity_Update"
BEFORE UPDATE OF "Engine", "Host", "Port", "DatabaseName" ON "DatabaseEndpoints"
WHEN EXISTS (
    SELECT 1
    FROM "DatabaseEndpoints" d
    WHERE d."Id" <> NEW."Id"
      AND d."Engine" = NEW."Engine"
      AND upper(rtrim(trim(d."Host"), '.')) = upper(rtrim(trim(NEW."Host"), '.'))
      AND (
          CASE
              WHEN d."Engine" = 1 AND instr(d."Host", '') = 0 THEN ifnull(d."Port", 1433)
              ELSE ifnull(d."Port", -1)
          END
      ) = (
          CASE
              WHEN NEW."Engine" = 1 AND instr(NEW."Host", '') = 0 THEN ifnull(NEW."Port", 1433)
              ELSE ifnull(NEW."Port", -1)
          END
      )
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
""");
    }
}
