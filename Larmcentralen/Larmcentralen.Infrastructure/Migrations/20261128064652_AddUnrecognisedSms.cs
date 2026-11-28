using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Larmcentralen.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261128064652_AddUnrecognisedSms : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UnrecognisedSms",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                From = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UnrecognisedSms", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_UnrecognisedSms_ReceivedAt",
            table: "UnrecognisedSms",
            column: "ReceivedAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "UnrecognisedSms");
    }
}
