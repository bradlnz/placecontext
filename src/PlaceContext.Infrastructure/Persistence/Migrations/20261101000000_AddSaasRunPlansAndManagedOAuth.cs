using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261101000000_AddSaasRunPlansAndManagedOAuth")]
public sealed class AddSaasRunPlansAndManagedOAuth : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS tenant_run_plans (
                "TenantId" uuid PRIMARY KEY REFERENCES tenants("Id") ON DELETE CASCADE,
                "Plan" text NOT NULL DEFAULT 'free',
                "Status" text NOT NULL DEFAULT 'active',
                "IncludedRuns" integer NOT NULL DEFAULT 100,
                "UsedRuns" integer NOT NULL DEFAULT 0,
                "PeriodStart" timestamptz NOT NULL,
                "PeriodEnd" timestamptz NOT NULL,
                "StripeCustomerId" text,
                "StripeSubscriptionId" text,
                "LastStripeEventCreated" bigint NOT NULL DEFAULT 0,
                CONSTRAINT ck_plan_usage CHECK ("IncludedRuns" >= 0 AND "UsedRuns" >= 0),
                CONSTRAINT ck_plan_period CHECK ("PeriodEnd" > "PeriodStart")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_tenant_run_plans_customer
                ON tenant_run_plans ("StripeCustomerId") WHERE "StripeCustomerId" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS ix_tenant_run_plans_subscription
                ON tenant_run_plans ("StripeSubscriptionId") WHERE "StripeSubscriptionId" IS NOT NULL;

            CREATE TABLE IF NOT EXISTS billable_runs (
                "TenantId" uuid NOT NULL REFERENCES tenants("Id") ON DELETE CASCADE,
                "RunKey" text NOT NULL,
                "ProjectId" uuid NOT NULL,
                "Kind" text NOT NULL,
                "ReservedAt" timestamptz NOT NULL,
                "PeriodStart" timestamptz NOT NULL,
                PRIMARY KEY ("TenantId", "RunKey"),
                CONSTRAINT ck_run_kind CHECK ("Kind" IN ('workflow', 'job'))
            );
            CREATE INDEX IF NOT EXISTS ix_billable_runs_period
                ON billable_runs ("TenantId", "PeriodStart");

            CREATE TABLE IF NOT EXISTS stripe_billing_events (
                "EventId" text PRIMARY KEY,
                "ProcessedAt" timestamptz NOT NULL
            );

            CREATE TABLE IF NOT EXISTS managed_oauth_connections (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL REFERENCES tenants("Id") ON DELETE CASCADE,
                "ProjectId" uuid NOT NULL,
                "Provider" text NOT NULL,
                "DisplayName" text NOT NULL,
                "AccessToken" text NOT NULL,
                "RefreshToken" text NOT NULL,
                "Scopes" text NOT NULL,
                "ExpiresAt" timestamptz NOT NULL,
                "UpdatedAt" timestamptz NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_managed_oauth_connection_project
                ON managed_oauth_connections ("TenantId", "ProjectId", "Provider");

            CREATE TABLE IF NOT EXISTS managed_oauth_states (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL REFERENCES tenants("Id") ON DELETE CASCADE,
                "ProjectId" uuid NOT NULL,
                "UserId" uuid NOT NULL,
                "Provider" text NOT NULL,
                "StateHash" text NOT NULL UNIQUE,
                "PkceVerifier" text NOT NULL,
                "ExpiresAt" timestamptz NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_managed_oauth_states_expiry
                ON managed_oauth_states ("ExpiresAt");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS managed_oauth_states;
            DROP TABLE IF EXISTS managed_oauth_connections;
            DROP TABLE IF EXISTS stripe_billing_events;
            DROP TABLE IF EXISTS billable_runs;
            DROP TABLE IF EXISTS tenant_run_plans;
            """);
    }
}
