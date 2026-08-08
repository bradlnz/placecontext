using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Vault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialVaultPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IF NOT EXISTS lets an existing gateway database adopt the Vault-owned migration
            // history without recreating the legacy table. A fresh Vault database receives the
            // same schema, while subsequent changes are owned exclusively by this context.
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS job_secrets (
                    "ProjectId" uuid NOT NULL,
                    "Name" character varying(200) NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "Cipher" text NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_job_secrets" PRIMARY KEY ("ProjectId", "Name")
                );
                CREATE INDEX IF NOT EXISTS "IX_job_secrets_TenantId"
                    ON job_secrets ("TenantId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_secrets");
        }
    }
}
