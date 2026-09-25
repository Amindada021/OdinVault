using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace OdinVault.Persistence.Migrations;

[DbContext(typeof(OdinVaultDbContext))]
[Migration("20260925210000_ReplicaContentHash")]
public sealed class ReplicaContentHash : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ContentHashSha256",
            table: "BackupReplicas",
            type: "TEXT",
            maxLength: 64,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ContentHashSha256",
            table: "BackupReplicas");
    }
}
