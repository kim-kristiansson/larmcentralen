using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Larmcentralen.Infrastructure.Migrations;

/// <summary>
/// Step 1 of 2 towards alarm definitions: adds the AlarmDefinitions and DefinitionSolutions tables and a
/// nullable Alarms.AlarmDefinitionId. Nothing is rebuilt and nothing is removed.
/// Between this and MoveAlarmTextToDefinitions, <see cref="Data.AlarmDefinitionBackfill"/> groups the existing
/// alarms into definitions and copies the solution links (SQLite cannot fold accents, so it runs in C#).
/// <see cref="Data.SqliteSetup.InitialiseAsync"/> runs the three in order.
/// </summary>
public partial class _20261121172910_AddAlarmDefinitions : Migration
{
    public const string Id = "20261121172910_AddAlarmDefinitions";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AlarmDefinitions",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                EquipmentType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                ScopeEquipmentId = table.Column<int>(type: "INTEGER", nullable: false),
                AlarmCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                NormalizedTitle = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AlarmDefinitions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "DefinitionSolutions",
            columns: table => new
            {
                AlarmDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                SolutionId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DefinitionSolutions", x => new { x.AlarmDefinitionId, x.SolutionId });
                table.ForeignKey(
                    name: "FK_DefinitionSolutions_AlarmDefinitions_AlarmDefinitionId",
                    column: x => x.AlarmDefinitionId,
                    principalTable: "AlarmDefinitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_DefinitionSolutions_Solutions_SolutionId",
                    column: x => x.SolutionId,
                    principalTable: "Solutions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AlarmDefinitions_EquipmentType_AlarmCode_NormalizedTitle_ScopeEquipmentId",
            table: "AlarmDefinitions",
            columns: new[] { "EquipmentType", "AlarmCode", "NormalizedTitle", "ScopeEquipmentId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DefinitionSolutions_SolutionId",
            table: "DefinitionSolutions",
            column: "SolutionId");

        // Nullable and without a foreign key for now, so SQLite can add it without rebuilding Alarms.
        // MoveAlarmTextToDefinitions makes it required once every alarm has a definition.
        migrationBuilder.Sql("ALTER TABLE \"Alarms\" ADD \"AlarmDefinitionId\" INTEGER NULL;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AlarmDefinitionId",
            table: "Alarms");

        migrationBuilder.DropTable(
            name: "DefinitionSolutions");

        migrationBuilder.DropTable(
            name: "AlarmDefinitions");
    }
}
