using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.AgentChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAgentChatPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing gateway databases already contain these tables. IF NOT EXISTS adopts that
            // schema into AgentChat's independent migration history without deleting tenant data.
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS agent_chat_sessions (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "ProjectId" uuid NOT NULL,
                    "UserId" uuid NULL,
                    "Title" text NULL,
                    "MessagesJson" text NOT NULL DEFAULT '[]',
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_agent_chat_sessions" PRIMARY KEY ("Id")
                );
                CREATE INDEX IF NOT EXISTS "IX_agent_chat_sessions_ProjectId_UpdatedAt"
                    ON agent_chat_sessions ("ProjectId", "UpdatedAt");

                CREATE TABLE IF NOT EXISTS agent_configs (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "ProjectId" uuid NOT NULL,
                    "BaseModel" text NOT NULL DEFAULT 'qwen3.5:0.8b',
                    "SystemPrompt" text NOT NULL DEFAULT '',
                    "Preamble" text NOT NULL DEFAULT '',
                    "ToolCatalog" text NOT NULL DEFAULT '',
                    "LaunchpadToolCatalog" text NOT NULL DEFAULT '',
                    "MaxContextChunks" integer NOT NULL DEFAULT 5,
                    "Temperature" real NOT NULL DEFAULT 0.7,
                    "TopP" real NOT NULL DEFAULT 0.9,
                    "Enabled" boolean NOT NULL DEFAULT TRUE,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_agent_configs" PRIMARY KEY ("Id")
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_agent_configs_ProjectId"
                    ON agent_configs ("ProjectId");

                CREATE TABLE IF NOT EXISTS chat_commands (
                    "Id" uuid NOT NULL,
                    "ProjectId" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "Name" character varying(100) NOT NULL,
                    "Description" text NULL,
                    "ToolName" character varying(100) NOT NULL,
                    "Args" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_chat_commands" PRIMARY KEY ("Id")
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_chat_commands_ProjectId_Name"
                    ON chat_commands ("ProjectId", "Name");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_chat_sessions");

            migrationBuilder.DropTable(
                name: "agent_configs");

            migrationBuilder.DropTable(
                name: "chat_commands");

            migrationBuilder.DropTable(
                name: "mcp_connections");
        }
    }
}
