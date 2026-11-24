using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Larmcentralen.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261124105250_AddContacts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ContactRoles",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ContactRoles", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Contacts",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                RoleId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Contacts", x => x.Id);
                table.ForeignKey(
                    name: "FK_Contacts_ContactRoles_RoleId",
                    column: x => x.RoleId,
                    principalTable: "ContactRoles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ContactRoles_Name",
            table: "ContactRoles",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Contacts_RoleId",
            table: "Contacts",
            column: "RoleId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Contacts");

        migrationBuilder.DropTable(
            name: "ContactRoles");
    }
}
