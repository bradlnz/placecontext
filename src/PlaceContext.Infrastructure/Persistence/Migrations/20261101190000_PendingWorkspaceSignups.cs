using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261101190000_PendingWorkspaceSignups")]
public sealed class PendingWorkspaceSignups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE pending_workspace_signups (
            "Id" uuid NOT NULL PRIMARY KEY,
            "Slug" text NOT NULL,
            "WorkspaceName" text NOT NULL,
            "Email" text NOT NULL,
            "DisplayName" text NOT NULL,
            "PasswordHash" text NOT NULL,
            "Plan" text NOT NULL,
            "TokenHash" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL,
            "ExpiresAt" timestamptz NOT NULL,
            "VerifiedAt" timestamptz NULL,
            "TenantId" uuid NULL,
            "UserId" uuid NULL,
            "ExchangeHash" text NULL,
            "ExchangeExpiresAt" timestamptz NULL,
            "ExchangeRedeemedAt" timestamptz NULL
        );
        CREATE UNIQUE INDEX ux_pending_signup_token ON pending_workspace_signups ("TokenHash");
        CREATE UNIQUE INDEX ux_pending_signup_exchange ON pending_workspace_signups ("ExchangeHash")
            WHERE "ExchangeHash" IS NOT NULL;
        CREATE INDEX ix_pending_signup_expiry ON pending_workspace_signups ("ExpiresAt");
        CREATE INDEX ix_pending_signup_slug ON pending_workspace_signups ("Slug");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE pending_workspace_signups;");
}
