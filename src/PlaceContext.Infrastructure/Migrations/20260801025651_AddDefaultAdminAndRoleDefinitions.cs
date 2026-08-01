using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultAdminAndRoleDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultAdmin",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "role_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    PermissionsJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_definitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_role_definitions_TenantId_Name",
                table: "role_definitions",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            // Stamp the bootstrap default admin for existing installs: per tenant, the earliest-created
            // user with a real (human-chosen) password and the Owner role — the same shape /setup
            // creates. The machine-provisioned "operator" row (PasswordSet = false) never qualifies.
            migrationBuilder.Sql("""
                UPDATE users SET "IsDefaultAdmin" = true
                WHERE "Id" IN (
                    SELECT DISTINCT ON ("TenantId") "Id"
                    FROM users
                    WHERE "PasswordSet" = true AND "Role" = 'Owner'
                    ORDER BY "TenantId", "CreatedAt" ASC);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_definitions");

            migrationBuilder.DropColumn(
                name: "IsDefaultAdmin",
                table: "users");
        }
    }
}
