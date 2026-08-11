using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentDefinitionHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentAgentId",
                table: "agent_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_definitions_ParentAgentId",
                table: "agent_definitions",
                column: "ParentAgentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_agent_definitions_ParentAgentId",
                table: "agent_definitions");

            migrationBuilder.DropColumn(
                name: "ParentAgentId",
                table: "agent_definitions");
        }
    }
}
