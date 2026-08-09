using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Communications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCommunicationsPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The legacy gateway database already owns this data. IF NOT EXISTS adopts the table
            // into Communications' migration history while also supporting a fresh service DB.
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS communication_providers (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "Channel" character varying(10) NOT NULL,
                    "Kind" character varying(20) NOT NULL,
                    "Name" character varying(100) NOT NULL,
                    "Enabled" boolean NOT NULL DEFAULT TRUE,
                    "IsDefault" boolean NOT NULL DEFAULT FALSE,
                    "UseForTwoFactor" boolean NOT NULL DEFAULT FALSE,
                    "AuthType" character varying(10) NOT NULL,
                    "AuthHeaderName" character varying(100) NULL,
                    "VaultProjectId" uuid NULL,
                    "ApiKeySecretName" character varying(200) NULL,
                    "SettingsJson" text NOT NULL DEFAULT '{}',
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT "PK_communication_providers" PRIMARY KEY ("Id")
                );
                CREATE INDEX IF NOT EXISTS "IX_communication_providers_TenantId_Channel"
                    ON communication_providers ("TenantId", "Channel");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "communication_providers");
        }
    }
}
