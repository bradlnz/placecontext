using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Projects.Infrastructure.Persistence.Migrations;

public partial class InitialProjectsPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing gateway databases already contain these tables. IF NOT EXISTS safely adopts
        // them into Projects' migration history and also supports a fresh standalone database.
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS projects (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "Name" text NOT NULL,
                "Path" text NOT NULL, "Status" text NOT NULL, "DiscoveredAt" timestamptz NOT NULL,
                "RegisteredAt" timestamptz NULL, "GraphJson" text NULL,
                CONSTRAINT "PK_projects" PRIMARY KEY ("Id"));
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_projects_TenantId_Path"
                ON projects ("TenantId", "Path");

            CREATE TABLE IF NOT EXISTS activity_log (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "Sequence" integer NOT NULL, "Summary" text NOT NULL, "AuthorName" text NOT NULL,
                "AuthorKind" text NOT NULL, "Rationale" text NOT NULL, "TestsAdded" integer NOT NULL,
                "TestsRemoved" integer NOT NULL, "TestsChanged" integer NOT NULL,
                "ArchReviewed" boolean NOT NULL, "LiveVerified" boolean NOT NULL,
                "TouchedFiles" text NOT NULL, "TouchedNodes" text NOT NULL,
                "CommitSha" text NULL, "RecordedAt" timestamptz NOT NULL,
                CONSTRAINT "PK_activity_log" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_activity_log_ProjectId_Sequence"
                ON activity_log ("ProjectId", "Sequence");

            CREATE TABLE IF NOT EXISTS decisions (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "Question" text NOT NULL, "Choice" text NOT NULL, "Rationale" text NOT NULL,
                "DecidedAt" timestamptz NOT NULL,
                CONSTRAINT "PK_decisions" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_decisions_ProjectId" ON decisions ("ProjectId");

            CREATE TABLE IF NOT EXISTS requirements (
                "ProjectId" uuid NOT NULL, "TenantId" uuid NOT NULL, "Markdown" text NOT NULL,
                "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NOT NULL,
                CONSTRAINT "PK_requirements" PRIMARY KEY ("TenantId", "ProjectId"));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "activity_log");
        migrationBuilder.DropTable(name: "decisions");
        migrationBuilder.DropTable(name: "projects");
        migrationBuilder.DropTable(name: "requirements");
    }
}
