using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShareExpiresAt",
                table: "build_conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShareTokenHash",
                table: "build_conversations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SharedContent",
                table: "build_conversations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_build_conversations_ShareTokenHash",
                table: "build_conversations",
                column: "ShareTokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_build_conversations_ShareTokenHash",
                table: "build_conversations");

            migrationBuilder.DropColumn(
                name: "ShareExpiresAt",
                table: "build_conversations");

            migrationBuilder.DropColumn(
                name: "ShareTokenHash",
                table: "build_conversations");

            migrationBuilder.DropColumn(
                name: "SharedContent",
                table: "build_conversations");
        }
    }
}
