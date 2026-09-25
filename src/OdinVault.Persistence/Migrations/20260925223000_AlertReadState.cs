using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace OdinVault.Persistence.Migrations;

[DbContext(typeof(OdinVaultDbContext))]
[Migration("20260925223000_AlertReadState")]
public sealed class AlertReadState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AlertReadStates",
            columns: table => new
            {
                AlertKey = table.Column<string>(
                    type: "TEXT",
                    maxLength: 300,
                    nullable: false),
                ReadAtUtc = table.Column<DateTime>(
                    type: "TEXT",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AlertReadStates", x => x.AlertKey);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AlertReadStates_ReadAtUtc",
            table: "AlertReadStates",
            column: "ReadAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AlertReadStates");
    }
}
