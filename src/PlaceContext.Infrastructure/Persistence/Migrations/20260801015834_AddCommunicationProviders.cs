using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunicationProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "communication_providers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    UseForTwoFactor = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    AuthType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AuthHeaderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VaultProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApiKeySecretName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SettingsJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_communication_providers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_communication_providers_TenantId_Channel",
                table: "communication_providers",
                columns: new[] { "TenantId", "Channel" });

            // Carry the singleton postmark_connections row (if present) over as the default,
            // 2FA-flagged email provider. Only the Vault *reference* moves — the token itself
            // stays in the project Vault.
            migrationBuilder.Sql(
                """
                INSERT INTO communication_providers
                    ("Id", "TenantId", "Channel", "Kind", "Name", "Enabled", "IsDefault", "UseForTwoFactor",
                     "AuthType", "AuthHeaderName", "VaultProjectId", "ApiKeySecretName", "SettingsJson",
                     "CreatedAt", "UpdatedAt")
                SELECT
                    gen_random_uuid(),
                    "TenantId",
                    'email',
                    'postmark',
                    'Postmark',
                    true,
                    true,
                    true,
                    'header',
                    'X-Postmark-Server-Token',
                    "VaultProjectId",
                    "ServerTokenSecretName",
                    jsonb_build_object(
                        'fromEmail', "FromEmail",
                        'fromName', "FromName",
                        'messageStream', "MessageStream")::text,
                    "ConfiguredAt",
                    "UpdatedAt"
                FROM postmark_connections;
                """);

            migrationBuilder.DropTable(
                name: "postmark_connections");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "communication_providers");

            migrationBuilder.CreateTable(
                name: "postmark_connections",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfiguredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    FromEmail = table.Column<string>(type: "text", nullable: false),
                    FromName = table.Column<string>(type: "text", nullable: false, defaultValue: "PlaceContext"),
                    MessageStream = table.Column<string>(type: "text", nullable: false, defaultValue: "outbound"),
                    ServerTokenSecretName = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    VaultProjectId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_postmark_connections", x => x.TenantId);
                });
        }
    }
}
