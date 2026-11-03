using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLaunchpadTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "JobId",
                table: "job_triggers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "ChainId",
                table: "job_triggers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Prompt",
                table: "job_triggers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceTable",
                table: "job_triggers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChainId",
                table: "job_triggers");

            migrationBuilder.DropColumn(
                name: "Prompt",
                table: "job_triggers");

            migrationBuilder.DropColumn(
                name: "SourceTable",
                table: "job_triggers");

            migrationBuilder.AlterColumn<Guid>(
                name: "JobId",
                table: "job_triggers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
