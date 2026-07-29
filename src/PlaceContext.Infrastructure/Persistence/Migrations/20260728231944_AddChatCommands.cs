using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChatCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CommandId",
                table: "job_triggers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "chat_commands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ToolName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Args = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_commands", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_commands_ProjectId_Name",
                table: "chat_commands",
                columns: new[] { "ProjectId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_commands");

            migrationBuilder.DropColumn(
                name: "CommandId",
                table: "job_triggers");
        }
    }
}
