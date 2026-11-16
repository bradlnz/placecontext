using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261117000000_CloudWorkspaces")]
public sealed class CloudWorkspaces : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE tenant_run_plans ADD COLUMN "UsedAiRequests" bigint NOT NULL DEFAULT 0;
        CREATE TABLE cloud_workspaces (
            "TenantId" uuid PRIMARY KEY CONSTRAINT "FK_cloud_workspaces_tenants_TenantId" REFERENCES tenants ("Id") ON DELETE CASCADE,
            "Plan" text NOT NULL,
            "State" text NOT NULL,
            "Attempts" integer NOT NULL DEFAULT 0,
            "NextAttemptAt" timestamptz NOT NULL,
            "LeaseToken" uuid NULL
        );
        CREATE INDEX "IX_cloud_workspaces_NextAttemptAt" ON cloud_workspaces ("NextAttemptAt");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE cloud_workspaces;
        ALTER TABLE tenant_run_plans DROP COLUMN "UsedAiRequests";
        """);
}
