using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameChangeRecordsToActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename in place — preserves existing rows (no drop/recreate).
            migrationBuilder.RenameTable(name: "change_records", newName: "activity_log");
            migrationBuilder.Sql(
                "ALTER TABLE activity_log RENAME CONSTRAINT \"PK_change_records\" TO \"PK_activity_log\";");
            migrationBuilder.RenameIndex(
                name: "IX_change_records_ProjectId_Sequence",
                newName: "IX_activity_log_ProjectId_Sequence",
                table: "activity_log");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_activity_log_ProjectId_Sequence",
                newName: "IX_change_records_ProjectId_Sequence",
                table: "change_records");
            migrationBuilder.Sql(
                "ALTER TABLE change_records RENAME CONSTRAINT \"PK_activity_log\" TO \"PK_change_records\";");
            migrationBuilder.RenameTable(name: "activity_log", newName: "change_records");
        }
    }
}
