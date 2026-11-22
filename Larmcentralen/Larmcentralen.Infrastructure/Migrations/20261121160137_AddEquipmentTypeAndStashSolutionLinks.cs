using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Larmcentralen.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261121160137_AddEquipmentTypeAndStashSolutionLinks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Solutions are moving to a many-to-many link table (AddAlarmSolutions). Keep every
        // (solution, alarm) pair here first: dropping Solutions.AlarmId rebuilds the table on SQLite.
        // Done in a migration of its own so no table references Solutions while it is rebuilt.
        migrationBuilder.Sql("CREATE TABLE \"_SolutionAlarmStash\" AS SELECT \"Id\", \"AlarmId\" FROM \"Solutions\";");

        migrationBuilder.DropForeignKey(
            name: "FK_Solutions_Alarms_AlarmId",
            table: "Solutions");

        migrationBuilder.DropIndex(
            name: "IX_Solutions_AlarmId",
            table: "Solutions");

        migrationBuilder.DropColumn(
            name: "AlarmId",
            table: "Solutions");

        migrationBuilder.AddColumn<string>(
            name: "EquipmentType",
            table: "Equipment",
            type: "TEXT",
            maxLength: 50,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "EquipmentType",
            table: "Equipment");

        migrationBuilder.AddColumn<int>(
            name: "AlarmId",
            table: "Solutions",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.Sql("""
            UPDATE "Solutions" SET "AlarmId" = COALESCE(
                (SELECT s."AlarmId" FROM "_SolutionAlarmStash" s WHERE s."Id" = "Solutions"."Id"), 0);
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Solutions_AlarmId",
            table: "Solutions",
            column: "AlarmId");

        migrationBuilder.AddForeignKey(
            name: "FK_Solutions_Alarms_AlarmId",
            table: "Solutions",
            column: "AlarmId",
            principalTable: "Alarms",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.Sql("DROP TABLE IF EXISTS \"_SolutionAlarmStash\";");
    }
}
