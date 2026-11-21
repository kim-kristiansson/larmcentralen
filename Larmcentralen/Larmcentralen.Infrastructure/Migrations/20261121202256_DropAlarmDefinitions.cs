using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Larmcentralen.Infrastructure.Migrations;

/// <summary>
/// Step 2 of 2 from alarm definitions to reusable alarm codes, after <see cref="Data.AlarmCodeBackfill"/> has filled
/// each alarm's code and text and stashed its solution links. Drops DefinitionSolutions first and creates
/// AlarmSolutions only after Alarms is rebuilt, so no table references Alarms while SQLite rebuilds it.
/// Alarm ids are kept.
/// </summary>
public partial class _20261121202256_DropAlarmDefinitions : Migration
{
    public const string Id = "20261121202256_DropAlarmDefinitions";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DefinitionSolutions");

        migrationBuilder.DropForeignKey(
            name: "FK_Alarms_AlarmDefinitions_AlarmDefinitionId",
            table: "Alarms");

        migrationBuilder.DropIndex(
            name: "IX_Alarms_AlarmDefinitionId",
            table: "Alarms");

        migrationBuilder.DropIndex(
            name: "IX_Alarms_EquipmentId",
            table: "Alarms");

        migrationBuilder.DropColumn(
            name: "AlarmDefinitionId",
            table: "Alarms");

        migrationBuilder.AlterColumn<string>(
            name: "Title",
            table: "Alarms",
            type: "TEXT",
            maxLength: 300,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "Severity",
            table: "Alarms",
            type: "TEXT",
            maxLength: 20,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Alarms_AlarmCodeId",
            table: "Alarms",
            column: "AlarmCodeId");

        migrationBuilder.CreateIndex(
            name: "IX_Alarms_EquipmentId_AlarmCodeId",
            table: "Alarms",
            columns: new[] { "EquipmentId", "AlarmCodeId" },
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_Alarms_AlarmCodes_AlarmCodeId",
            table: "Alarms",
            column: "AlarmCodeId",
            principalTable: "AlarmCodes",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
        // Going back would rebuild alarm definitions from per-unit text, which cannot be done faithfully. Restore the copy
        // that SqliteSetup writes next to the database before migrating (larmcentralen.before-migration-*.db) instead.
        => throw new NotSupportedException("DropAlarmDefinitions cannot be reverted. Restore the backup written before migrating.");
}
