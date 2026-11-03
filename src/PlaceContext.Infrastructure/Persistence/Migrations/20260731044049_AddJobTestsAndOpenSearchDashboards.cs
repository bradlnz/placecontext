using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobTestsAndOpenSearchDashboards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_test_cases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    InputPayload = table.Column<string>(type: "text", nullable: true),
                    AssertionType = table.Column<string>(type: "text", nullable: false, defaultValue: "Succeeds"),
                    ExpectedValue = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    LastStatus = table.Column<string>(type: "text", nullable: false, defaultValue: "NotRun"),
                    LastMessage = table.Column<string>(type: "text", nullable: true),
                    LastActualOutput = table.Column<string>(type: "text", nullable: true),
                    LastJobRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastDurationMs = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_test_cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_job_test_cases_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "opensearch_dashboards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IndexPattern = table.Column<string>(type: "text", nullable: false, defaultValue: "*"),
                    QueryText = table.Column<string>(type: "text", nullable: true),
                    BucketField = table.Column<string>(type: "text", nullable: false),
                    BucketType = table.Column<string>(type: "text", nullable: false, defaultValue: "terms"),
                    ChartType = table.Column<string>(type: "text", nullable: false, defaultValue: "bar"),
                    MetricType = table.Column<string>(type: "text", nullable: false, defaultValue: "count"),
                    MetricField = table.Column<string>(type: "text", nullable: true),
                    DateInterval = table.Column<string>(type: "text", nullable: true),
                    ChartSpecJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opensearch_dashboards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_test_cases_JobId_Name",
                table: "job_test_cases",
                columns: new[] { "JobId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_test_cases_ProjectId_JobId",
                table: "job_test_cases",
                columns: new[] { "ProjectId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_opensearch_dashboards_ProjectId_Name",
                table: "opensearch_dashboards",
                columns: new[] { "ProjectId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_test_cases");

            migrationBuilder.DropTable(
                name: "opensearch_dashboards");
        }
    }
}
