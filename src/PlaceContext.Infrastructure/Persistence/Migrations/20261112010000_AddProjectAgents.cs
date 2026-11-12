using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261112010000_AddProjectAgents")]
public sealed class AddProjectAgents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "AgentSettings", table: "projects", type: "text", nullable: true);
        migrationBuilder.AddColumn<int>(name: "AgentSettingsVersion", table: "projects", type: "integer", nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AgentSettings", table: "projects");
        migrationBuilder.DropColumn(name: "AgentSettingsVersion", table: "projects");
    }
}
