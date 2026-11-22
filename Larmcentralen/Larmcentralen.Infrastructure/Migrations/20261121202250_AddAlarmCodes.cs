using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Larmcentralen.Infrastructure.Migrations;

/// <summary>
/// Step 1 of 2 from alarm definitions to reusable alarm codes: adds the AlarmCodes table, nullable per-alarm
/// AlarmCodeId, Title, Severity and Description columns, and a stash for solution links. Nothing is rebuilt or
/// removed. Between this and DropAlarmDefinitions, <see cref="Data.AlarmCodeBackfill"/> fills the codes, copies each
/// definition's text onto its alarms and expands its solution links per alarm into the stash.
/// <see cref="Data.SqliteSetup.MigrateAsync"/> runs the three in order.
/// </summary>
public partial class _20261121202250_AddAlarmCodes : Migration
{
    public const string Id = "20261121202250_AddAlarmCodes";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AlarmCodes",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AlarmCodes", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AlarmCodes_Code",
            table: "AlarmCodes",
            column: "Code",
            unique: true);

        // Plain nullable columns and a stash without foreign keys, so SQLite adds them without rebuilding Alarms.
        // DropAlarmDefinitions makes Title and Severity required and adds the foreign key once they are filled.
        migrationBuilder.Sql("""
            ALTER TABLE "Alarms" ADD "AlarmCodeId" INTEGER NULL;
            ALTER TABLE "Alarms" ADD "Title" TEXT NULL;
            ALTER TABLE "Alarms" ADD "Severity" TEXT NULL;
            ALTER TABLE "Alarms" ADD "Description" TEXT NULL;
            CREATE TABLE "_AlarmSolutionStash" ("AlarmId" INTEGER NOT NULL, "SolutionId" INTEGER NOT NULL);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS \"_AlarmSolutionStash\";");
        migrationBuilder.DropColumn(name: "AlarmCodeId", table: "Alarms");
        migrationBuilder.DropColumn(name: "Title", table: "Alarms");
        migrationBuilder.DropColumn(name: "Severity", table: "Alarms");
        migrationBuilder.DropColumn(name: "Description", table: "Alarms");
        migrationBuilder.DropTable(name: "AlarmCodes");
    }
}
