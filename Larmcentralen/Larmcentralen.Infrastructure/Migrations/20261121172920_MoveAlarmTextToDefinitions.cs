using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Larmcentralen.Infrastructure.Migrations;

/// <summary>
/// Step 2 of 2 towards alarm definitions, after <see cref="Data.AlarmDefinitionBackfill"/> has given every alarm
/// a definition and copied the solution links to DefinitionSolutions. Drops AlarmSolutions first, so no table
/// references Alarms while SQLite rebuilds it, then removes the text columns that moved to the definition and
/// makes Alarms.AlarmDefinitionId required. Alarm ids are kept.
/// </summary>
public partial class _20261121172920_MoveAlarmTextToDefinitions : Migration
{
    public const string Id = "20261121172920_MoveAlarmTextToDefinitions";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AlarmSolutions");

        migrationBuilder.DropIndex(
            name: "IX_Alarms_AlarmCode_EquipmentId",
            table: "Alarms");

        migrationBuilder.DropColumn(
            name: "AlarmCode",
            table: "Alarms");

        migrationBuilder.DropColumn(
            name: "Description",
            table: "Alarms");

        migrationBuilder.DropColumn(
            name: "Severity",
            table: "Alarms");

        migrationBuilder.DropColumn(
            name: "Title",
            table: "Alarms");

        migrationBuilder.AlterColumn<int>(
            name: "AlarmDefinitionId",
            table: "Alarms",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Alarms_AlarmDefinitionId",
            table: "Alarms",
            column: "AlarmDefinitionId");

        migrationBuilder.AddForeignKey(
            name: "FK_Alarms_AlarmDefinitions_AlarmDefinitionId",
            table: "Alarms",
            column: "AlarmDefinitionId",
            principalTable: "AlarmDefinitions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
        // Going back would need the old per-alarm text columns rebuilt on SQLite. Restore the copy that
        // SqliteSetup writes next to the database before migrating (larmcentralen.before-migration-*.db) instead.
        => throw new NotSupportedException("MoveAlarmTextToDefinitions cannot be reverted. Restore the backup written before migrating.");
}
