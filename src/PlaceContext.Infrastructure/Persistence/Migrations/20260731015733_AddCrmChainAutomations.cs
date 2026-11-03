using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmChainAutomations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "crm_chain_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    LifecycleStage = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_chain_runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_crm_chain_runs_ChainRunId",
                table: "crm_chain_runs",
                column: "ChainRunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_crm_chain_runs_ClientId_StartedAt",
                table: "crm_chain_runs",
                columns: new[] { "ClientId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "crm_chain_runs");
        }
    }
}
