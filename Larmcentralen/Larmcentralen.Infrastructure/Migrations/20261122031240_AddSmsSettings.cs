using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Larmcentralen.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261122031240_AddSmsSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SmsSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                ElksNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                SenderNumbers = table.Column<string>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                LastSmsAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                LastSmsFrom = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                LastSmsResult = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                LastSmsAccepted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SmsSettings", x => x.Id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SmsSettings");
    }
}
