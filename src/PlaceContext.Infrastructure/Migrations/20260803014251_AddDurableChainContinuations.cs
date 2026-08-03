using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableChainContinuations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContinuationClaimedAt",
                table: "chain_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContinuationClaimedBy",
                table: "chain_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CrmClientId",
                table: "chain_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResumeAt",
                table: "chain_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResumeStageIndex",
                table: "chain_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_chain_runs_Status_ResumeAt_ContinuationClaimedAt",
                table: "chain_runs",
                columns: new[] { "Status", "ResumeAt", "ContinuationClaimedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_chain_runs_Status_ResumeAt_ContinuationClaimedAt",
                table: "chain_runs");

            migrationBuilder.DropColumn(
                name: "ContinuationClaimedAt",
                table: "chain_runs");

            migrationBuilder.DropColumn(
                name: "ContinuationClaimedBy",
                table: "chain_runs");

            migrationBuilder.DropColumn(
                name: "CrmClientId",
                table: "chain_runs");

            migrationBuilder.DropColumn(
                name: "ResumeAt",
                table: "chain_runs");

            migrationBuilder.DropColumn(
                name: "ResumeStageIndex",
                table: "chain_runs");
        }
    }
}
