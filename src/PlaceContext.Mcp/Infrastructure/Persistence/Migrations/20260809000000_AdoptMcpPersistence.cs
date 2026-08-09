using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PlaceContext.Mcp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(McpDbContext))]
[Migration("20260809000000_AdoptMcpPersistence")]
public sealed class AdoptMcpPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS mcp_connections (
                "Id" uuid NOT NULL,
                "ProjectId" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "Name" character varying(100) NOT NULL,
                "Transport" character varying(20) NOT NULL,
                "EndpointUrl" character varying(500) NULL,
                "Command" character varying(200) NULL,
                "Args" character varying(1000) NULL,
                "AuthType" text NULL,
                "AuthToken" text NULL,
                "AuthHeader" text NULL,
                "Enabled" boolean NOT NULL,
                "LastStatus" character varying(200) NULL,
                "LastConnectedAt" timestamp with time zone NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "OAuthAccessToken" text NULL,
                "OAuthRefreshToken" text NULL,
                "OAuthTokenExpiresAt" timestamp with time zone NULL,
                "OAuthClientId" character varying(200) NULL,
                "OAuthScopes" character varying(500) NULL,
                CONSTRAINT "PK_mcp_connections" PRIMARY KEY ("Id")
            );
            """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This migration adopts a table that can predate the MCP service. A rollback must not
        // delete connection or OAuth-token data still used by the previous application version.
    }
}
