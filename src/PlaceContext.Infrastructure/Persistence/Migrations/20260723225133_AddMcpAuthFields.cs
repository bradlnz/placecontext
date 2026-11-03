using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpAuthFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthHeader",
                table: "mcp_connections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthToken",
                table: "mcp_connections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthType",
                table: "mcp_connections",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthHeader",
                table: "mcp_connections");

            migrationBuilder.DropColumn(
                name: "AuthToken",
                table: "mcp_connections");

            migrationBuilder.DropColumn(
                name: "AuthType",
                table: "mcp_connections");
        }
    }
}
