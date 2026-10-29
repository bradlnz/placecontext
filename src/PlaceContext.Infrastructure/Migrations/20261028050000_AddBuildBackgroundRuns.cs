using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations;

public partial class AddBuildBackgroundRuns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Status", table: "build_conversations", type: "text", nullable: false, defaultValue: "Idle");
        migrationBuilder.AddColumn<bool>(name: "StopRequested", table: "build_conversations", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>(name: "AutoDiscovered", table: "model_connections", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.CreateIndex(name: "IX_build_conversations_Status_UpdatedAt", table: "build_conversations", columns: new[] { "Status", "UpdatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_build_conversations_Status_UpdatedAt", table: "build_conversations");
        migrationBuilder.DropColumn(name: "Status", table: "build_conversations");
        migrationBuilder.DropColumn(name: "StopRequested", table: "build_conversations");
        migrationBuilder.DropColumn(name: "AutoDiscovered", table: "model_connections");
    }
}
