using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOcrTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OcrError",
                table: "job_run_artifacts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OcrProcessedAt",
                table: "job_run_artifacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_run_artifacts_ocr",
                table: "job_run_artifacts",
                column: "OcrProcessedAt",
                filter: "\"OcrProcessedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_run_artifacts_ocr",
                table: "job_run_artifacts");

            migrationBuilder.DropColumn(
                name: "OcrError",
                table: "job_run_artifacts");

            migrationBuilder.DropColumn(
                name: "OcrProcessedAt",
                table: "job_run_artifacts");
        }
    }
}
