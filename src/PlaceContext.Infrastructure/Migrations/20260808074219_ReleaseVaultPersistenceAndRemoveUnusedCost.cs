using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseVaultPersistenceAndRemoveUnusedCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS usage_records;

                CREATE TABLE IF NOT EXISTS crm_client_job_chain_assignments (
                    "Id" uuid NOT NULL CONSTRAINT "PK_crm_client_job_chain_assignments" PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "ProjectId" uuid NOT NULL,
                    "ClientId" uuid NOT NULL,
                    "ChainId" uuid NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now()
                );
                CREATE INDEX IF NOT EXISTS "IX_crm_client_job_chain_assignments_ChainId"
                    ON crm_client_job_chain_assignments ("ChainId");
                CREATE INDEX IF NOT EXISTS "IX_crm_client_job_chain_assignments_ProjectId_ClientId"
                    ON crm_client_job_chain_assignments ("ProjectId", "ClientId");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_crm_client_job_chain_assignments_ProjectId_ClientId_ChainId"
                    ON crm_client_job_chain_assignments ("ProjectId", "ClientId", "ChainId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS crm_client_job_chain_assignments;

                CREATE TABLE IF NOT EXISTS usage_records (
                    "Id" uuid NOT NULL CONSTRAINT "PK_usage_records" PRIMARY KEY,
                    "Description" text NULL,
                    "InputTokens" bigint NOT NULL,
                    "Model" text NOT NULL,
                    "OutputTokens" bigint NOT NULL,
                    "ProjectId" uuid NOT NULL,
                    "RecordedAt" timestamp with time zone NOT NULL,
                    "TenantId" uuid NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_usage_records_ProjectId_RecordedAt"
                    ON usage_records ("ProjectId", "RecordedAt");
                """);
        }
    }
}
