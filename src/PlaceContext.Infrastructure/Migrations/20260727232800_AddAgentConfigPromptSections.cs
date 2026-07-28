using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentConfigPromptSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LaunchpadToolCatalog",
                table: "agent_configs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Preamble",
                table: "agent_configs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ToolCatalog",
                table: "agent_configs",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LaunchpadToolCatalog",
                table: "agent_configs");

            migrationBuilder.DropColumn(
                name: "Preamble",
                table: "agent_configs");

            migrationBuilder.DropColumn(
                name: "ToolCatalog",
                table: "agent_configs");
        }
    }
}
