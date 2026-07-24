using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpOAuthFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OAuthAccessToken",
                table: "mcp_connections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthClientId",
                table: "mcp_connections",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthRefreshToken",
                table: "mcp_connections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthScopes",
                table: "mcp_connections",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OAuthTokenExpiresAt",
                table: "mcp_connections",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OAuthAccessToken",
                table: "mcp_connections");

            migrationBuilder.DropColumn(
                name: "OAuthClientId",
                table: "mcp_connections");

            migrationBuilder.DropColumn(
                name: "OAuthRefreshToken",
                table: "mcp_connections");

            migrationBuilder.DropColumn(
                name: "OAuthScopes",
                table: "mcp_connections");

            migrationBuilder.DropColumn(
                name: "OAuthTokenExpiresAt",
                table: "mcp_connections");
        }
    }
}
