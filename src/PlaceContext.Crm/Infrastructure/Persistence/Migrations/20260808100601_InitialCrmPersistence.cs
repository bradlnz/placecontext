using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Crm.Infrastructure.Persistence.Migrations;

public partial class InitialCrmPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing gateway databases already contain these tables. IF NOT EXISTS safely adopts
        // them into CRM's migration history and also supports a fresh standalone CRM database.
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS crm_clients (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "Name" text NOT NULL, "Company" text NULL, "Email" text NULL, "Phone" text NULL,
                "LifecycleStage" text NOT NULL DEFAULT 'Lead', "Notes" text NULL,
                "CustomerPortalEnabled" boolean NOT NULL DEFAULT FALSE,
                "CustomerPortalSlug" text NULL, "CustomerPortalDomain" text NULL,
                "CustomerPortalBrandName" text NULL, "CustomerPortalLogoUrl" text NULL,
                "CreatedAt" timestamptz NOT NULL DEFAULT now(),
                "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT "PK_crm_clients" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_crm_clients_ProjectId_Email" ON crm_clients ("ProjectId", "Email");
            CREATE INDEX IF NOT EXISTS "IX_crm_clients_ProjectId_LifecycleStage"
                ON crm_clients ("ProjectId", "LifecycleStage");

            CREATE TABLE IF NOT EXISTS crm_job_runs (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "ClientId" uuid NOT NULL, "JobId" uuid NOT NULL, "RunId" uuid NOT NULL,
                "LifecycleStage" text NOT NULL, "StartedAt" timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT "PK_crm_job_runs" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_crm_job_runs_ClientId_StartedAt"
                ON crm_job_runs ("ClientId", "StartedAt");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_crm_job_runs_RunId" ON crm_job_runs ("RunId");

            CREATE TABLE IF NOT EXISTS crm_chain_runs (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "ClientId" uuid NOT NULL, "ChainId" uuid NOT NULL, "ChainRunId" uuid NOT NULL,
                "LifecycleStage" text NOT NULL, "StartedAt" timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT "PK_crm_chain_runs" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_crm_chain_runs_ClientId_StartedAt"
                ON crm_chain_runs ("ClientId", "StartedAt");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_crm_chain_runs_ChainRunId" ON crm_chain_runs ("ChainRunId");

            CREATE TABLE IF NOT EXISTS crm_communications (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "ClientId" uuid NOT NULL, "Channel" text NOT NULL, "SubjectProtected" text NULL,
                "BodyProtected" text NOT NULL, "RecipientProtected" text NULL, "Status" text NOT NULL,
                "Provider" text NULL, "ExternalId" text NULL, "ErrorProtected" text NULL,
                "CreatedByUserId" uuid NOT NULL, "CreatedAt" timestamptz NOT NULL DEFAULT now(),
                "SentAt" timestamptz NULL, CONSTRAINT "PK_crm_communications" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_crm_communications_ClientId_CreatedAt"
                ON crm_communications ("ClientId", "CreatedAt");

            CREATE TABLE IF NOT EXISTS crm_appointments (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "CalendarId" uuid NULL, "ClientId" uuid NULL, "TitleProtected" text NOT NULL,
                "StartsAt" timestamptz NOT NULL, "EndsAt" timestamptz NOT NULL,
                "LocationProtected" text NULL, "NotesProtected" text NULL,
                "CreatedByUserId" uuid NOT NULL, "CreatedAt" timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT "PK_crm_appointments" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_crm_appointments_CalendarId" ON crm_appointments ("CalendarId");
            CREATE INDEX IF NOT EXISTS "IX_crm_appointments_ClientId" ON crm_appointments ("ClientId");
            CREATE INDEX IF NOT EXISTS "IX_crm_appointments_ProjectId_StartsAt"
                ON crm_appointments ("ProjectId", "StartsAt");

            CREATE TABLE IF NOT EXISTS crm_calendars (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "Name" text NOT NULL, "Color" text NOT NULL,
                "CreatedAt" timestamptz NOT NULL DEFAULT now(),
                "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT "PK_crm_calendars" PRIMARY KEY ("Id"));
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_crm_calendars_ProjectId_Name"
                ON crm_calendars ("ProjectId", "Name");

            CREATE TABLE IF NOT EXISTS crm_client_artifacts (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "ClientId" uuid NOT NULL, "SourceArtifactId" uuid NULL, "ChainRunId" uuid NULL,
                "Title" text NOT NULL, "Bucket" text NOT NULL, "ObjectKey" text NOT NULL,
                "ContentType" text NOT NULL, "SizeBytes" bigint NOT NULL,
                "CreatedAt" timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT "PK_crm_client_artifacts" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_crm_client_artifacts_ClientId_CreatedAt"
                ON crm_client_artifacts ("ClientId", "CreatedAt");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_crm_client_artifacts_ClientId_SourceArtifactId"
                ON crm_client_artifacts ("ClientId", "SourceArtifactId");

            CREATE TABLE IF NOT EXISTS crm_client_job_chain_assignments (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "ClientId" uuid NOT NULL, "ChainId" uuid NOT NULL,
                "CreatedAt" timestamptz NOT NULL DEFAULT now(),
                "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT "PK_crm_client_job_chain_assignments" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_crm_client_job_chain_assignments_ChainId"
                ON crm_client_job_chain_assignments ("ChainId");
            CREATE INDEX IF NOT EXISTS "IX_crm_client_job_chain_assignments_ProjectId_ClientId"
                ON crm_client_job_chain_assignments ("ProjectId", "ClientId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_crm_client_job_chain_assignments_ProjectId_ClientId_ChainId"
                ON crm_client_job_chain_assignments ("ProjectId", "ClientId", "ChainId");

            CREATE TABLE IF NOT EXISTS crm_automation_rules (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "Name" text NOT NULL, "EventType" text NOT NULL, "LifecycleStage" text NULL,
                "ChainId" uuid NOT NULL, "Enabled" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamptz NOT NULL DEFAULT now(),
                "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT "PK_crm_automation_rules" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_crm_automation_rules_ProjectId_EventType_LifecycleStage"
                ON crm_automation_rules ("ProjectId", "EventType", "LifecycleStage");

            CREATE TABLE IF NOT EXISTS crm_automation_queue (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "RuleId" uuid NOT NULL, "ClientId" uuid NULL, "ChainId" uuid NOT NULL,
                "EventType" text NOT NULL, "LifecycleStage" text NULL, "RuleName" text NOT NULL,
                "InputPayloadProtected" text NULL, "EnqueuedAt" timestamptz NOT NULL,
                "NextAttemptAt" timestamptz NOT NULL, "Attempts" integer NOT NULL,
                "LastError" text NULL, "ClaimedBy" text NULL, "ClaimedAt" timestamptz NULL,
                "FailedAt" timestamptz NULL, "ChainRunId" uuid NULL, "ResultStatus" text NULL,
                "CompletedAt" timestamptz NULL,
                CONSTRAINT "PK_crm_automation_queue" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_crm_automation_queue_ChainRunId" ON crm_automation_queue ("ChainRunId");
            CREATE INDEX IF NOT EXISTS "IX_crm_automation_queue_CompletedAt_FailedAt_ClaimedAt_NextAtt~"
                ON crm_automation_queue ("CompletedAt", "FailedAt", "ClaimedAt", "NextAttemptAt");
            CREATE INDEX IF NOT EXISTS "IX_crm_automation_queue_TenantId_ProjectId_Id"
                ON crm_automation_queue ("TenantId", "ProjectId", "Id");

            CREATE TABLE IF NOT EXISTS crm_ingestion_settings (
                "ProjectId" uuid NOT NULL, "TenantId" uuid NOT NULL, "AllowedOrigin" text NOT NULL,
                "TokenHash" text NULL, "TokenPrefix" text NULL,
                "CreatedAt" timestamptz NOT NULL DEFAULT now(),
                "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT "PK_crm_ingestion_settings" PRIMARY KEY ("ProjectId"));
            CREATE INDEX IF NOT EXISTS "IX_crm_ingestion_settings_AllowedOrigin"
                ON crm_ingestion_settings ("AllowedOrigin");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_crm_ingestion_settings_TokenHash"
                ON crm_ingestion_settings ("TokenHash");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "crm_appointments");
        migrationBuilder.DropTable(name: "crm_automation_queue");
        migrationBuilder.DropTable(name: "crm_automation_rules");
        migrationBuilder.DropTable(name: "crm_calendars");
        migrationBuilder.DropTable(name: "crm_chain_runs");
        migrationBuilder.DropTable(name: "crm_client_artifacts");
        migrationBuilder.DropTable(name: "crm_client_job_chain_assignments");
        migrationBuilder.DropTable(name: "crm_clients");
        migrationBuilder.DropTable(name: "crm_communications");
        migrationBuilder.DropTable(name: "crm_ingestion_settings");
        migrationBuilder.DropTable(name: "crm_job_runs");
    }
}
