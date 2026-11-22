using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Larmcentralen.Infrastructure.Migrations;

/// <summary>
/// Last step from alarm definitions to reusable alarm codes, after DropAlarmDefinitions has rebuilt Alarms:
/// drops AlarmDefinitions (nothing references it any more) and turns the stashed per-alarm solution links into
/// the AlarmSolutions table.
/// </summary>
public partial class _20261121202258_RestoreAlarmSolutions : Migration
{
    public const string Id = "20261121202258_RestoreAlarmSolutions";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AlarmDefinitions");

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

        migrationBuilder.Sql("""
            INSERT OR IGNORE INTO "AlarmSolutions" ("AlarmId", "SolutionId")
            SELECT s."AlarmId", s."SolutionId" FROM "_AlarmSolutionStash" s
            WHERE s."AlarmId" IN (SELECT "Id" FROM "Alarms") AND s."SolutionId" IN (SELECT "Id" FROM "Solutions");
            """);
        migrationBuilder.Sql("DROP TABLE \"_AlarmSolutionStash\";");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("RestoreAlarmSolutions cannot be reverted. Restore the backup written before migrating.");
}
