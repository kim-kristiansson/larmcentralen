using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Larmcentralen.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261122024237_AddTrash : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "TrashBatchId",
            table: "Solutions",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TrashBatchId",
            table: "Equipment",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TrashBatchId",
            table: "Areas",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TrashBatchId",
            table: "Alarms",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "TrashBatches",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                AreaTitle = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                EquipmentCount = table.Column<int>(type: "INTEGER", nullable: false),
                AlarmCount = table.Column<int>(type: "INTEGER", nullable: false),
                SolutionCount = table.Column<int>(type: "INTEGER", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TrashBatches", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Solutions_TrashBatchId",
            table: "Solutions",
            column: "TrashBatchId");

        migrationBuilder.CreateIndex(
            name: "IX_Equipment_TrashBatchId",
            table: "Equipment",
            column: "TrashBatchId");

        migrationBuilder.CreateIndex(
            name: "IX_Areas_TrashBatchId",
            table: "Areas",
            column: "TrashBatchId");

        migrationBuilder.CreateIndex(
            name: "IX_Alarms_TrashBatchId",
            table: "Alarms",
            column: "TrashBatchId");

        migrationBuilder.AddForeignKey(
            name: "FK_Alarms_TrashBatches_TrashBatchId",
            table: "Alarms",
            column: "TrashBatchId",
            principalTable: "TrashBatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Areas_TrashBatches_TrashBatchId",
            table: "Areas",
            column: "TrashBatchId",
            principalTable: "TrashBatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Equipment_TrashBatches_TrashBatchId",
            table: "Equipment",
            column: "TrashBatchId",
            principalTable: "TrashBatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Solutions_TrashBatches_TrashBatchId",
            table: "Solutions",
            column: "TrashBatchId",
            principalTable: "TrashBatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Alarms_TrashBatches_TrashBatchId",
            table: "Alarms");

        migrationBuilder.DropForeignKey(
            name: "FK_Areas_TrashBatches_TrashBatchId",
            table: "Areas");

        migrationBuilder.DropForeignKey(
            name: "FK_Equipment_TrashBatches_TrashBatchId",
            table: "Equipment");

        migrationBuilder.DropForeignKey(
            name: "FK_Solutions_TrashBatches_TrashBatchId",
            table: "Solutions");

        migrationBuilder.DropTable(
            name: "TrashBatches");

        migrationBuilder.DropIndex(
            name: "IX_Solutions_TrashBatchId",
            table: "Solutions");

        migrationBuilder.DropIndex(
            name: "IX_Equipment_TrashBatchId",
            table: "Equipment");

        migrationBuilder.DropIndex(
            name: "IX_Areas_TrashBatchId",
            table: "Areas");

        migrationBuilder.DropIndex(
            name: "IX_Alarms_TrashBatchId",
            table: "Alarms");

        migrationBuilder.DropColumn(
            name: "TrashBatchId",
            table: "Solutions");

        migrationBuilder.DropColumn(
            name: "TrashBatchId",
            table: "Equipment");

        migrationBuilder.DropColumn(
            name: "TrashBatchId",
            table: "Areas");

        migrationBuilder.DropColumn(
            name: "TrashBatchId",
            table: "Alarms");
    }
}
