using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "change_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    AuthorName = table.Column<string>(type: "text", nullable: false),
                    AuthorKind = table.Column<string>(type: "text", nullable: false),
                    Rationale = table.Column<string>(type: "text", nullable: false),
                    TestsAdded = table.Column<int>(type: "integer", nullable: false),
                    TestsRemoved = table.Column<int>(type: "integer", nullable: false),
                    TestsChanged = table.Column<int>(type: "integer", nullable: false),
                    DebtResolved = table.Column<int>(type: "integer", nullable: false),
                    DebtIntroduced = table.Column<int>(type: "integer", nullable: false),
                    ArchReviewed = table.Column<bool>(type: "boolean", nullable: false),
                    LiveVerified = table.Column<bool>(type: "boolean", nullable: false),
                    TouchedFiles = table.Column<string>(type: "text", nullable: false),
                    TouchedNodes = table.Column<string>(type: "text", nullable: false),
                    CommitSha = table.Column<string>(type: "text", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_change_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "code_requirements",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Markdown = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_code_requirements", x => new { x.TenantId, x.ProjectId });
                });

            migrationBuilder.CreateTable(
                name: "debt_assessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Technical = table.Column<double>(type: "double precision", nullable: false),
                    Agentic = table.Column<double>(type: "double precision", nullable: false),
                    Signals = table.Column<string>(type: "text", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_debt_assessments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "decisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Question = table.Column<string>(type: "text", nullable: false),
                    Choice = table.Column<string>(type: "text", nullable: false),
                    Rationale = table.Column<string>(type: "text", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "oauth_clients",
                columns: table => new
                {
                    ClientId = table.Column<string>(type: "text", nullable: false),
                    RedirectUris = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_clients", x => x.ClientId);
                });

            migrationBuilder.CreateTable(
                name: "project_contexts",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Markdown = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_contexts", x => x.ProjectId);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DiscoveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GraphJson = table.Column<string>(type: "text", nullable: true),
                    TechnicalDebt = table.Column<double>(type: "double precision", nullable: true),
                    AgenticDebt = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TimeZoneId = table.Column<string>(type: "text", nullable: false),
                    GitHubLogin = table.Column<string>(type: "text", nullable: true),
                    GitHubToken = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tool_calls",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tool = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    Project = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    RequestJson = table.Column<string>(type: "text", nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_calls", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "usage_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "work_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_items", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_change_records_ProjectId_Sequence",
                table: "change_records",
                columns: new[] { "ProjectId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_debt_assessments_ProjectId_ComputedAt",
                table: "debt_assessments",
                columns: new[] { "ProjectId", "ComputedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_decisions_ProjectId",
                table: "decisions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_TenantId_Path",
                table: "projects",
                columns: new[] { "TenantId", "Path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_Slug",
                table: "tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tool_calls_At",
                table: "tool_calls",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_usage_records_ProjectId_RecordedAt",
                table: "usage_records",
                columns: new[] { "ProjectId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_users_TenantId_Email",
                table: "users",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_items_ProjectId_Status",
                table: "work_items",
                columns: new[] { "ProjectId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "change_records");

            migrationBuilder.DropTable(
                name: "code_requirements");

            migrationBuilder.DropTable(
                name: "debt_assessments");

            migrationBuilder.DropTable(
                name: "decisions");

            migrationBuilder.DropTable(
                name: "oauth_clients");

            migrationBuilder.DropTable(
                name: "project_contexts");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropTable(
                name: "tool_calls");

            migrationBuilder.DropTable(
                name: "usage_records");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "work_items");
        }
    }
}
