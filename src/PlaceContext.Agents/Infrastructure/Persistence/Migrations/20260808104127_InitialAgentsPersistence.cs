using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Agents.Infrastructure.Persistence.Migrations;

public partial class InitialAgentsPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS agent_approvals (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "AssignmentId" uuid NOT NULL,
                "ActionKind" character varying(80) NOT NULL,
                "Summary" text NOT NULL,
                "PayloadJson" text NOT NULL,
                "Status" character varying(24) NOT NULL,
                "ResolvedByUserId" uuid NULL,
                "ReviewerComment" text NOT NULL,
                "RequestedAt" timestamp with time zone NOT NULL,
                "ResolvedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_agent_approvals" PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS "IX_agent_approvals_AssignmentId"
                ON agent_approvals ("AssignmentId");
            CREATE INDEX IF NOT EXISTS "IX_agent_approvals_TenantId_Status_RequestedAt"
                ON agent_approvals ("TenantId", "Status", "RequestedAt");

            CREATE TABLE IF NOT EXISTS agent_assignments (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "StaffMemberId" uuid NOT NULL,
                "ProjectId" uuid NOT NULL,
                "ParentAssignmentId" uuid NULL,
                "DelegatedByStaffMemberId" uuid NULL,
                "ScheduleId" uuid NULL,
                "CreatedByUserId" uuid NOT NULL,
                "Objective" text NOT NULL,
                "ProfileVersion" integer NOT NULL,
                "Status" character varying(32) NOT NULL,
                "ScheduledFor" timestamp with time zone NULL,
                "PlanSummary" text NOT NULL,
                "ResultSummary" text NOT NULL,
                "FailureSummary" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_agent_assignments" PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS "IX_agent_assignments_ParentAssignmentId"
                ON agent_assignments ("ParentAssignmentId");
            CREATE INDEX IF NOT EXISTS "IX_agent_assignments_StaffMemberId_Status"
                ON agent_assignments ("StaffMemberId", "Status");
            CREATE INDEX IF NOT EXISTS "IX_agent_assignments_TenantId_ProjectId_CreatedAt"
                ON agent_assignments ("TenantId", "ProjectId", "CreatedAt");

            CREATE TABLE IF NOT EXISTS agent_profiles (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "Name" character varying(120) NOT NULL,
                "Role" character varying(120) NOT NULL,
                "Description" text NOT NULL,
                "Responsibilities" text NOT NULL,
                "SystemInstructions" text NOT NULL,
                "Provider" character varying(80) NOT NULL,
                "Model" character varying(160) NOT NULL,
                "ReasoningLevel" character varying(40) NOT NULL,
                "AllowedToolsJson" text NOT NULL,
                "AllowedJobIdsJson" text NOT NULL,
                "AllowedJobChainIdsJson" text NOT NULL,
                "SkillsJson" text NOT NULL,
                "PermissionsJson" text NOT NULL,
                "RequirePlanApproval" boolean NOT NULL,
                "RequireExternalActionApproval" boolean NOT NULL,
                "RequireJobDraftApproval" boolean NOT NULL,
                "MaxTokensPerAssignment" bigint NOT NULL,
                "MaxCostPerAssignment" numeric(18,4) NOT NULL,
                "MaxExecutionMinutes" integer NOT NULL,
                "MaxRetries" integer NOT NULL,
                "MaxDelegationDepth" integer NOT NULL,
                "ConcurrencyLimit" integer NOT NULL,
                "Version" integer NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_agent_profiles" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_agent_profiles_TenantId_Name"
                ON agent_profiles ("TenantId", "Name");

            CREATE TABLE IF NOT EXISTS agent_staff (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "ProfileId" uuid NOT NULL,
                "Name" character varying(120) NOT NULL,
                "ProjectIdsJson" text NOT NULL,
                "InstructionsOverride" text NOT NULL,
                "ModelOverride" character varying(160) NULL,
                "Status" character varying(24) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_agent_staff" PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS "IX_agent_staff_ProfileId"
                ON agent_staff ("ProfileId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_agent_staff_TenantId_Name"
                ON agent_staff ("TenantId", "Name");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "agent_approvals");
        migrationBuilder.DropTable(name: "agent_assignments");
        migrationBuilder.DropTable(name: "agent_profiles");
        migrationBuilder.DropTable(name: "agent_staff");
    }
}
