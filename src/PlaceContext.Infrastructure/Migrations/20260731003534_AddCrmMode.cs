using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "crm_clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Company = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    LifecycleStage = table.Column<string>(type: "text", nullable: false, defaultValue: "Lead"),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "crm_job_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    LifecycleStage = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_job_runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_crm_clients_ProjectId_Email",
                table: "crm_clients",
                columns: new[] { "ProjectId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_clients_ProjectId_LifecycleStage",
                table: "crm_clients",
                columns: new[] { "ProjectId", "LifecycleStage" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_job_runs_ClientId_StartedAt",
                table: "crm_job_runs",
                columns: new[] { "ClientId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_job_runs_RunId",
                table: "crm_job_runs",
                column: "RunId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "crm_clients");

            migrationBuilder.DropTable(
                name: "crm_job_runs");
        }
    }
}
