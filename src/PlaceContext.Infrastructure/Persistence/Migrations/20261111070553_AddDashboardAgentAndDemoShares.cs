using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardAgentAndDemoShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Settings",
                table: "build_memories",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "workflow_demo_shares",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_demo_shares", x => new { x.TenantId, x.UserId, x.RunId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_demo_shares_TokenHash",
                table: "workflow_demo_shares",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_demo_shares");

            migrationBuilder.DropColumn(
                name: "Settings",
                table: "build_memories");
        }
    }
}
