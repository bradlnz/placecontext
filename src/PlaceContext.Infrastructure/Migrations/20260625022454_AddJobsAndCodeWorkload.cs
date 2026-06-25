using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobsAndCodeWorkload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ShardResultsJson = table.Column<string>(type: "text", nullable: false),
                    ReduceResultJson = table.Column<string>(type: "text", nullable: true),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    MapSourceKind = table.Column<string>(type: "text", nullable: false, defaultValue: "image"),
                    MapImage = table.Column<string>(type: "text", nullable: true),
                    MapRuntimeId = table.Column<string>(type: "text", nullable: true),
                    MapSource = table.Column<string>(type: "text", nullable: true),
                    MapEntrypoint = table.Column<string>(type: "text", nullable: true),
                    InputPayloadsJson = table.Column<string>(type: "text", nullable: false),
                    MapEnvJson = table.Column<string>(type: "text", nullable: false),
                    ReduceSourceKind = table.Column<string>(type: "text", nullable: true),
                    ReduceImage = table.Column<string>(type: "text", nullable: true),
                    ReduceRuntimeId = table.Column<string>(type: "text", nullable: true),
                    ReduceSource = table.Column<string>(type: "text", nullable: true),
                    ReduceEntrypoint = table.Column<string>(type: "text", nullable: true),
                    ReduceEnvJson = table.Column<string>(type: "text", nullable: true),
                    SuccessCodesJson = table.Column<string>(type: "text", nullable: false),
                    PartialCodesJson = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyLimit = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_runs_JobId_StartedAt",
                table: "job_runs",
                columns: new[] { "JobId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_job_runs_ProjectId",
                table: "job_runs",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_ProjectId",
                table: "jobs",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_runs");

            migrationBuilder.DropTable(
                name: "jobs");
        }
    }
}
