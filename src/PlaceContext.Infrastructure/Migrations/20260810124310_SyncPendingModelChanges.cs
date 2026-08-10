using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "crm_client_job_chain_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_client_job_chain_assignments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_crm_client_job_chain_assignments_ChainId",
                table: "crm_client_job_chain_assignments",
                column: "ChainId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_client_job_chain_assignments_ProjectId_ClientId",
                table: "crm_client_job_chain_assignments",
                columns: new[] { "ProjectId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_client_job_chain_assignments_ProjectId_ClientId_ChainId",
                table: "crm_client_job_chain_assignments",
                columns: new[] { "ProjectId", "ClientId", "ChainId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "crm_client_job_chain_assignments");
        }
    }
}
