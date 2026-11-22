using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Larmcentralen.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261122042611_AddSmsElksLogin : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ElksPasswordProtected",
            table: "SmsSettings",
            type: "TEXT",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ElksUsername",
            table: "SmsSettings",
            type: "TEXT",
            maxLength: 100,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ElksPasswordProtected",
            table: "SmsSettings");

        migrationBuilder.DropColumn(
            name: "ElksUsername",
            table: "SmsSettings");
    }
}
