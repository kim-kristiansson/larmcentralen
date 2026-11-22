using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Larmcentralen.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261121160201_AddAlarmSolutions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AlarmSolutions",
            columns: table => new
            {
                AlarmId = table.Column<int>(type: "INTEGER", nullable: false),
                SolutionId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AlarmSolutions", x => new { x.AlarmId, x.SolutionId });
                table.ForeignKey(
                    name: "FK_AlarmSolutions_Alarms_AlarmId",
                    column: x => x.AlarmId,
                    principalTable: "Alarms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AlarmSolutions_Solutions_SolutionId",
                    column: x => x.SolutionId,
                    principalTable: "Solutions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AlarmSolutions_SolutionId",
            table: "AlarmSolutions",
            column: "SolutionId");

        // Move the links kept by AddEquipmentTypeAndStashSolutionLinks into the new table, then drop the stash.
        migrationBuilder.Sql("""
            INSERT INTO "AlarmSolutions" ("AlarmId", "SolutionId")
            SELECT st."AlarmId", st."Id" FROM "_SolutionAlarmStash" st
            WHERE st."AlarmId" IN (SELECT "Id" FROM "Alarms") AND st."Id" IN (SELECT "Id" FROM "Solutions");
            """);
        migrationBuilder.Sql("DROP TABLE \"_SolutionAlarmStash\";");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Back to one alarm per solution: keep the lowest linked alarm for the previous migration's Down.
        migrationBuilder.Sql("""
            CREATE TABLE "_SolutionAlarmStash" AS
            SELECT "SolutionId" AS "Id", MIN("AlarmId") AS "AlarmId" FROM "AlarmSolutions" GROUP BY "SolutionId";
            """);

        migrationBuilder.DropTable(
            name: "AlarmSolutions");
    }
}
