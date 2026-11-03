using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmAutomationTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_crm_automation_queue_FailedAt_ClaimedAt_NextAttemptAt",
                table: "crm_automation_queue");

            migrationBuilder.AddColumn<Guid>(
                name: "ChainRunId",
                table: "crm_automation_queue",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "crm_automation_queue",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "crm_automation_queue",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE crm_automation_queue AS queue
                SET "ProjectId" = rules."ProjectId"
                FROM crm_automation_rules AS rules
                WHERE rules."Id" = queue."RuleId"
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM crm_automation_queue
                WHERE "ProjectId" IS NULL
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                table: "crm_automation_queue",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultStatus",
                table: "crm_automation_queue",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_crm_automation_queue_ChainRunId",
                table: "crm_automation_queue",
                column: "ChainRunId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_automation_queue_CompletedAt_FailedAt_ClaimedAt_NextAtt~",
                table: "crm_automation_queue",
                columns: new[] { "CompletedAt", "FailedAt", "ClaimedAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_automation_queue_TenantId_ProjectId_Id",
                table: "crm_automation_queue",
                columns: new[] { "TenantId", "ProjectId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_crm_automation_queue_ChainRunId",
                table: "crm_automation_queue");

            migrationBuilder.DropIndex(
                name: "IX_crm_automation_queue_CompletedAt_FailedAt_ClaimedAt_NextAtt~",
                table: "crm_automation_queue");

            migrationBuilder.DropIndex(
                name: "IX_crm_automation_queue_TenantId_ProjectId_Id",
                table: "crm_automation_queue");

            migrationBuilder.DropColumn(
                name: "ChainRunId",
                table: "crm_automation_queue");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "crm_automation_queue");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "crm_automation_queue");

            migrationBuilder.DropColumn(
                name: "ResultStatus",
                table: "crm_automation_queue");

            migrationBuilder.CreateIndex(
                name: "IX_crm_automation_queue_FailedAt_ClaimedAt_NextAttemptAt",
                table: "crm_automation_queue",
                columns: new[] { "FailedAt", "ClaimedAt", "NextAttemptAt" });
        }
    }
}
