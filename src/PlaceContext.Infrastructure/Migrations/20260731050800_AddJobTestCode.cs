using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobTestCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowNetworkEgress",
                table: "job_test_cases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CodeFilesJson",
                table: "job_test_cases",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Entrypoint",
                table: "job_test_cases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuntimeId",
                table: "job_test_cases",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowNetworkEgress",
                table: "job_test_cases");

            migrationBuilder.DropColumn(
                name: "CodeFilesJson",
                table: "job_test_cases");

            migrationBuilder.DropColumn(
                name: "Entrypoint",
                table: "job_test_cases");

            migrationBuilder.DropColumn(
                name: "RuntimeId",
                table: "job_test_cases");
        }
    }
}
