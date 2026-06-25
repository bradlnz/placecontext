using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameDebtToRisk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename in place — preserves existing rows (no drop/recreate).
            migrationBuilder.RenameTable(name: "debt_assessments", newName: "risk_assessments");
            migrationBuilder.Sql(
                "ALTER TABLE risk_assessments RENAME CONSTRAINT \"PK_debt_assessments\" TO \"PK_risk_assessments\";");
            migrationBuilder.RenameColumn(
                name: "Agentic", table: "risk_assessments", newName: "Process");
            migrationBuilder.RenameIndex(
                name: "IX_debt_assessments_ProjectId_ComputedAt",
                newName: "IX_risk_assessments_ProjectId_ComputedAt",
                table: "risk_assessments");

            migrationBuilder.RenameColumn(
                name: "TechnicalDebt",
                table: "projects",
                newName: "TechnicalRisk");

            migrationBuilder.RenameColumn(
                name: "AgenticDebt",
                table: "projects",
                newName: "ProcessRisk");

            migrationBuilder.RenameColumn(
                name: "DebtResolved",
                table: "change_records",
                newName: "RiskResolved");

            migrationBuilder.RenameColumn(
                name: "DebtIntroduced",
                table: "change_records",
                newName: "RiskIntroduced");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TechnicalRisk",
                table: "projects",
                newName: "TechnicalDebt");

            migrationBuilder.RenameColumn(
                name: "ProcessRisk",
                table: "projects",
                newName: "AgenticDebt");

            migrationBuilder.RenameColumn(
                name: "RiskResolved",
                table: "change_records",
                newName: "DebtResolved");

            migrationBuilder.RenameColumn(
                name: "RiskIntroduced",
                table: "change_records",
                newName: "DebtIntroduced");

            migrationBuilder.RenameIndex(
                name: "IX_risk_assessments_ProjectId_ComputedAt",
                newName: "IX_debt_assessments_ProjectId_ComputedAt",
                table: "risk_assessments");
            migrationBuilder.RenameColumn(
                name: "Process", table: "risk_assessments", newName: "Agentic");
            migrationBuilder.Sql(
                "ALTER TABLE risk_assessments RENAME CONSTRAINT \"PK_risk_assessments\" TO \"PK_debt_assessments\";");
            migrationBuilder.RenameTable(name: "risk_assessments", newName: "debt_assessments");
        }
    }
}
