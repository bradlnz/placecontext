using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmCalendarsAndEventEditing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CalendarId",
                table: "crm_appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "crm_calendars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Color = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_calendars", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_crm_appointments_CalendarId",
                table: "crm_appointments",
                column: "CalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_calendars_ProjectId_Name",
                table: "crm_calendars",
                columns: new[] { "ProjectId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "crm_calendars");

            migrationBuilder.DropIndex(
                name: "IX_crm_appointments_CalendarId",
                table: "crm_appointments");

            migrationBuilder.DropColumn(
                name: "CalendarId",
                table: "crm_appointments");
        }
    }
}
