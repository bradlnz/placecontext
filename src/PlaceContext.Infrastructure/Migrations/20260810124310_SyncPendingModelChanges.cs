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
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS crm_client_job_chain_assignments (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "ProjectId" uuid NOT NULL,
                    "ClientId" uuid NOT NULL,
                    "ChainId" uuid NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now()
                );

                DO $$
                DECLARE
                    has_primary_key boolean;
                BEGIN
                    SELECT EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conrelid = 'crm_client_job_chain_assignments'::regclass
                          AND contype = 'p'
                    )
                    INTO has_primary_key;

                    IF NOT has_primary_key THEN
                        ALTER TABLE crm_client_job_chain_assignments
                        ADD CONSTRAINT "PK_crm_client_job_chain_assignments" PRIMARY KEY ("Id");
                    END IF;
                END $$;

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
            migrationBuilder.DropTable(
                name: "crm_client_job_chain_assignments");
        }
    }
}
