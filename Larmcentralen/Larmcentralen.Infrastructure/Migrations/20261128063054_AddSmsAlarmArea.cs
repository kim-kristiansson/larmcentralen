using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Larmcentralen.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261128063054_AddSmsAlarmArea : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AlarmAreaId",
            table: "SmsSettings",
            type: "INTEGER",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AlarmAreaId",
            table: "SmsSettings");
    }
}
