using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Jobs.Infrastructure.Persistence.Migrations;

public partial class InitialJobsPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Gateway databases already contain these tables. IF NOT EXISTS adopts them into the
        // Jobs-specific migration history while also supporting a fresh standalone Jobs database.
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS jobs (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "Name" text NOT NULL, "Description" text NULL,
                "MapSourceKind" text NOT NULL DEFAULT 'image', "MapImage" text NULL,
                "MapRuntimeId" text NULL, "MapSource" text NULL, "MapFilesJson" text NULL,
                "MapEntrypoint" text NULL, "InputPayloadsJson" text NOT NULL,
                "MapEnvJson" text NOT NULL, "ReduceSourceKind" text NULL,
                "ReduceImage" text NULL, "ReduceRuntimeId" text NULL, "ReduceSource" text NULL,
                "ReduceFilesJson" text NULL, "ReduceEntrypoint" text NULL,
                "ReduceEnvJson" text NULL, "SuccessCodesJson" text NOT NULL,
                "PartialCodesJson" text NOT NULL, "ConcurrencyLimit" integer NOT NULL,
                "ParametersJson" text NOT NULL DEFAULT '[]',
                "PostJobActionsJson" text NOT NULL DEFAULT '[]',
                "ReturnType" text NOT NULL DEFAULT 'Json', "ReturnFileName" text NULL,
                "AllowNetworkEgress" boolean NOT NULL DEFAULT FALSE,
                "AllowApiInvocation" boolean NOT NULL DEFAULT FALSE,
                "TimeoutSeconds" integer NOT NULL DEFAULT 1800,
                "RetryCount" integer NOT NULL DEFAULT 0,
                "RetryDelaySeconds" integer NOT NULL DEFAULT 0,
                "McpConnectionIdsJson" text NOT NULL,
                "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT "PK_jobs" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_jobs_ProjectId" ON jobs ("ProjectId");

            CREATE TABLE IF NOT EXISTS job_test_cases (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "JobId" uuid NOT NULL, "Name" text NOT NULL, "InputPayload" text NULL,
                "AssertionType" text NOT NULL DEFAULT 'Succeeds', "ExpectedValue" text NULL,
                "Enabled" boolean NOT NULL DEFAULT TRUE, "LastStatus" text NOT NULL DEFAULT 'NotRun',
                "LastMessage" text NULL, "LastActualOutput" text NULL, "LastJobRunId" uuid NULL,
                "LastRunAt" timestamptz NULL, "LastDurationMs" bigint NULL,
                "CreatedAt" timestamptz NOT NULL DEFAULT now(),
                "UpdatedAt" timestamptz NOT NULL DEFAULT now(), "RuntimeId" text NULL,
                "Entrypoint" text NULL, "CodeFilesJson" text NOT NULL DEFAULT '[]',
                "AllowNetworkEgress" boolean NOT NULL DEFAULT FALSE,
                "MethodResultsJson" text NOT NULL DEFAULT '[]',
                CONSTRAINT "PK_job_test_cases" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_job_test_cases_jobs_JobId" FOREIGN KEY ("JobId")
                    REFERENCES jobs ("Id") ON DELETE CASCADE);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_job_test_cases_JobId_Name"
                ON job_test_cases ("JobId", "Name");
            CREATE INDEX IF NOT EXISTS "IX_job_test_cases_ProjectId_JobId"
                ON job_test_cases ("ProjectId", "JobId");

            CREATE TABLE IF NOT EXISTS job_runs (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "JobId" uuid NOT NULL,
                "ProjectId" uuid NOT NULL, "Status" text NOT NULL,
                "StartedAt" timestamptz NOT NULL, "FinishedAt" timestamptz NULL,
                "ShardCount" integer NOT NULL, "SucceededShards" integer NOT NULL,
                "PartialShards" integer NOT NULL, "FailedShards" integer NOT NULL,
                "ShardResultsJson" text NOT NULL, "ReduceResultJson" text NULL,
                "SnapshotJson" text NOT NULL DEFAULT '{}',
                "AttemptNumber" integer NOT NULL DEFAULT 1, "OriginalRunId" uuid NULL,
                CONSTRAINT "PK_job_runs" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_job_runs_JobId_StartedAt" ON job_runs ("JobId", "StartedAt");
            CREATE INDEX IF NOT EXISTS "IX_job_runs_ProjectId" ON job_runs ("ProjectId");
            CREATE INDEX IF NOT EXISTS ix_job_runs_active ON job_runs ("Status")
                WHERE "Status" IN ('Queued', 'Running');
            CREATE INDEX IF NOT EXISTS ix_job_runs_finished_at ON job_runs ("FinishedAt");
            CREATE INDEX IF NOT EXISTS ix_job_runs_tenant_started ON job_runs ("TenantId", "StartedAt");

            CREATE TABLE IF NOT EXISTS job_triggers (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "JobId" uuid NULL, "Name" text NOT NULL, "Kind" text NOT NULL,
                "Enabled" boolean NOT NULL, "CronExpression" text NULL, "EventName" text NULL,
                "ChainId" uuid NULL, "SourceTable" text NULL, "Prompt" text NULL,
                "CommandId" uuid NULL, "NextRunAt" timestamptz NULL, "LastFiredAt" timestamptz NULL,
                "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NOT NULL,
                CONSTRAINT "PK_job_triggers" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_job_triggers_Enabled_Kind_NextRunAt"
                ON job_triggers ("Enabled", "Kind", "NextRunAt");
            CREATE INDEX IF NOT EXISTS "IX_job_triggers_JobId" ON job_triggers ("JobId");

            CREATE TABLE IF NOT EXISTS job_chains (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "Name" text NOT NULL, "Description" text NULL, "StagesJson" text NOT NULL,
                "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NOT NULL,
                CONSTRAINT "PK_job_chains" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_job_chains_ProjectId" ON job_chains ("ProjectId");

            CREATE TABLE IF NOT EXISTS chain_runs (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ChainId" uuid NOT NULL,
                "ProjectId" uuid NOT NULL, "ChainName" text NOT NULL, "Status" text NOT NULL,
                "StepsJson" text NOT NULL, "FinalOutput" text NULL,
                "StartedAt" timestamptz NOT NULL, "FinishedAt" timestamptz NULL,
                "ResumeAt" timestamptz NULL, "ResumeStageIndex" integer NULL,
                "CrmClientId" uuid NULL, "ContinuationClaimedBy" text NULL,
                "ContinuationClaimedAt" timestamptz NULL, "ContinuationOverrides" text NULL,
                CONSTRAINT "PK_chain_runs" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_chain_runs_ChainId_StartedAt" ON chain_runs ("ChainId", "StartedAt");
            CREATE INDEX IF NOT EXISTS "IX_chain_runs_ProjectId" ON chain_runs ("ProjectId");
            CREATE INDEX IF NOT EXISTS "IX_chain_runs_Status_ResumeAt_ContinuationClaimedAt"
                ON chain_runs ("Status", "ResumeAt", "ContinuationClaimedAt");
            CREATE INDEX IF NOT EXISTS ix_chain_runs_active ON chain_runs ("Status")
                WHERE "Status" IN ('Queued', 'Running');
            CREATE INDEX IF NOT EXISTS ix_chain_runs_finished_at ON chain_runs ("FinishedAt");
            CREATE INDEX IF NOT EXISTS ix_chain_runs_tenant_started ON chain_runs ("TenantId", "StartedAt");

            CREATE TABLE IF NOT EXISTS event_definitions (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "Name" text NOT NULL,
                "Description" text NULL, "PayloadSchema" text NULL,
                "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NOT NULL,
                CONSTRAINT "PK_event_definitions" PRIMARY KEY ("Id"));
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_event_definitions_TenantId_Name"
                ON event_definitions ("TenantId", "Name");

            CREATE TABLE IF NOT EXISTS event_occurrences (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "Name" text NOT NULL,
                "Source" text NOT NULL, "ProjectId" uuid NULL, "Payload" text NULL,
                "OccurredAt" timestamptz NOT NULL,
                CONSTRAINT "PK_event_occurrences" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_event_occurrences_Name_OccurredAt"
                ON event_occurrences ("Name", "OccurredAt");

            CREATE TABLE IF NOT EXISTS pending_job_runs (
                "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "JobId" uuid NOT NULL,
                "TriggerId" uuid NOT NULL, "TriggerName" text NOT NULL, "Payload" text NULL,
                "EnqueuedAt" timestamptz NOT NULL, "ClaimedBy" text NULL, "ClaimedAt" timestamptz NULL,
                CONSTRAINT "PK_pending_job_runs" PRIMARY KEY ("Id"));
            CREATE INDEX IF NOT EXISTS "IX_pending_job_runs_ClaimedAt_EnqueuedAt"
                ON pending_job_runs ("ClaimedAt", "EnqueuedAt");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "chain_runs");
        migrationBuilder.DropTable(name: "event_definitions");
        migrationBuilder.DropTable(name: "event_occurrences");
        migrationBuilder.DropTable(name: "job_chains");
        migrationBuilder.DropTable(name: "job_runs");
        migrationBuilder.DropTable(name: "job_test_cases");
        migrationBuilder.DropTable(name: "job_triggers");
        migrationBuilder.DropTable(name: "pending_job_runs");
        migrationBuilder.DropTable(name: "jobs");
    }
}
