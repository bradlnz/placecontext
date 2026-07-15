using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncSecurityModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: may already have been applied manually (sms drop / user_api_tokens).
            migrationBuilder.Sql("""DROP TABLE IF EXISTS sms_messages;""");

            migrationBuilder.Sql("""
                ALTER TABLE invites ADD COLUMN IF NOT EXISTS "ExpiresAt" timestamp with time zone NULL;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS user_api_tokens (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "Name" text NOT NULL,
                    "TokenHash" text NOT NULL,
                    "TokenPrefix" text NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "LastUsedAt" timestamp with time zone NULL,
                    "ExpiresAt" timestamp with time zone NULL,
                    "RevokedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_user_api_tokens" PRIMARY KEY ("Id")
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_user_api_tokens_TokenHash"
                    ON user_api_tokens ("TokenHash");
                CREATE INDEX IF NOT EXISTS "IX_user_api_tokens_UserId_CreatedAt"
                    ON user_api_tokens ("UserId", "CreatedAt");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "user_api_tokens");
            migrationBuilder.DropColumn(name: "ExpiresAt", table: "invites");
        }
    }
}
