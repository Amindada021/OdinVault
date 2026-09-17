using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace OdinVault.Persistence.Migrations;

[DbContext(typeof(OdinVaultDbContext))]
[Migration("20260917020000_Baseline")]
public sealed class Baseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "DatabaseEndpoints" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_DatabaseEndpoints" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Engine" INTEGER NOT NULL,
    "Host" TEXT NOT NULL,
    "Port" INTEGER NULL,
    "DatabaseName" TEXT NOT NULL,
    "Username" TEXT NOT NULL,
    "ProtectedPassword" TEXT NULL,
    "TrustServerCertificate" INTEGER NOT NULL,
    "IsEnabled" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS "BackupPolicies" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_BackupPolicies" PRIMARY KEY,
    "DatabaseEndpointId" TEXT NOT NULL,
    "BackupDirectory" TEXT NOT NULL,
    "ScheduleCron" TEXT NULL,
    "MaxLocalBackups" INTEGER NOT NULL,
    "VerifyAfterBackup" INTEGER NOT NULL,
    "IsEnabled" INTEGER NOT NULL,
    "LastScheduledRunUtc" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "BackupRecords" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_BackupRecords" PRIMARY KEY,
    "DatabaseEndpointId" TEXT NOT NULL,
    "FileName" TEXT NOT NULL,
    "FilePath" TEXT NOT NULL,
    "LocalFileAvailable" INTEGER NOT NULL,
    "LocalFileDeletedAtUtc" TEXT NULL,
    "Status" INTEGER NOT NULL,
    "VerificationStatus" INTEGER NOT NULL,
    "SizeBytes" INTEGER NULL,
    "StartedAtUtc" TEXT NOT NULL,
    "CompletedAtUtc" TEXT NULL,
    "Error" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "StorageTargets" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_StorageTargets" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Type" INTEGER NOT NULL,
    "IsEnabled" INTEGER NOT NULL,
    "FolderId" TEXT NULL,
    "AccountEmail" TEXT NULL,
    "ProtectedRefreshToken" TEXT NULL,
    "BaseUrl" TEXT NULL,
    "ProtectedApiKey" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS "DatabaseStorageTargets" (
    "DatabaseEndpointId" TEXT NOT NULL,
    "StorageTargetId" TEXT NOT NULL,
    "IsEnabled" INTEGER NOT NULL,
    CONSTRAINT "PK_DatabaseStorageTargets" PRIMARY KEY ("DatabaseEndpointId", "StorageTargetId")
);
CREATE TABLE IF NOT EXISTS "BackupReplicas" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_BackupReplicas" PRIMARY KEY,
    "BackupRecordId" TEXT NOT NULL,
    "StorageTargetId" TEXT NOT NULL,
    "Status" INTEGER NOT NULL,
    "RemoteId" TEXT NULL,
    "RemotePath" TEXT NULL,
    "SizeBytes" INTEGER NULL,
    "Error" TEXT NULL,
    "StartedAtUtc" TEXT NOT NULL,
    "CompletedAtUtc" TEXT NULL
);
CREATE INDEX IF NOT EXISTS "IX_DatabaseEndpoints_Name" ON "DatabaseEndpoints" ("Name");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_BackupPolicies_DatabaseEndpointId" ON "BackupPolicies" ("DatabaseEndpointId");
CREATE INDEX IF NOT EXISTS "IX_BackupRecords_DatabaseEndpointId_StartedAtUtc" ON "BackupRecords" ("DatabaseEndpointId", "StartedAtUtc");
CREATE INDEX IF NOT EXISTS "IX_StorageTargets_Name" ON "StorageTargets" ("Name");
CREATE INDEX IF NOT EXISTS "IX_DatabaseStorageTargets_StorageTargetId" ON "DatabaseStorageTargets" ("StorageTargetId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_BackupReplicas_BackupRecordId_StorageTargetId" ON "BackupReplicas" ("BackupRecordId", "StorageTargetId");
CREATE INDEX IF NOT EXISTS "IX_BackupReplicas_StorageTargetId_StartedAtUtc" ON "BackupReplicas" ("StorageTargetId", "StartedAtUtc");
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DROP TABLE IF EXISTS "BackupReplicas";
DROP TABLE IF EXISTS "DatabaseStorageTargets";
DROP TABLE IF EXISTS "StorageTargets";
DROP TABLE IF EXISTS "BackupRecords";
DROP TABLE IF EXISTS "BackupPolicies";
DROP TABLE IF EXISTS "DatabaseEndpoints";
""");
    }
}
