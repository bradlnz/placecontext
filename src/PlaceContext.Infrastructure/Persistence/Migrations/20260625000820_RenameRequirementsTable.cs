using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameRequirementsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename in place — preserves existing rows (no drop/recreate).
            migrationBuilder.RenameTable(name: "code_requirements", newName: "requirements");
            migrationBuilder.Sql(
                "ALTER TABLE requirements RENAME CONSTRAINT \"PK_code_requirements\" TO \"PK_requirements\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE requirements RENAME CONSTRAINT \"PK_requirements\" TO \"PK_code_requirements\";");
            migrationBuilder.RenameTable(name: "requirements", newName: "code_requirements");
        }
    }
}
