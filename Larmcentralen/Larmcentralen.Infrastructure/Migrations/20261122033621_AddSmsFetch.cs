using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Larmcentralen.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261122033621_AddSmsFetch : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "FetchedUntil",
            table: "SmsSettings",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "LastFetchAt",
            table: "SmsSettings",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LastFetchError",
            table: "SmsSettings",
            type: "TEXT",
            maxLength: 500,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FetchedUntil",
            table: "SmsSettings");

        migrationBuilder.DropColumn(
            name: "LastFetchAt",
            table: "SmsSettings");

        migrationBuilder.DropColumn(
            name: "LastFetchError",
            table: "SmsSettings");
    }
}
