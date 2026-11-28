using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Larmcentralen.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261128063915_RemoveSmsAlarmArea : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AlarmAreaId",
            table: "SmsSettings");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AlarmAreaId",
            table: "SmsSettings",
            type: "INTEGER",
            nullable: true);
    }
}
