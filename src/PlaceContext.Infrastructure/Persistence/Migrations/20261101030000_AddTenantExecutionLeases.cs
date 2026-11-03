using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261101030000_AddTenantExecutionLeases")]
public sealed class AddTenantExecutionLeases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            CREATE TABLE tenant_execution_leases (
                "Token" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL REFERENCES tenants("Id") ON DELETE CASCADE,
                "RunKey" text NOT NULL,
                "AcquiredAt" timestamptz NOT NULL,
                "ExpiresAt" timestamptz NOT NULL,
                CONSTRAINT ux_tenant_execution_run UNIQUE ("TenantId", "RunKey")
            );
            CREATE INDEX ix_tenant_execution_expiry
                ON tenant_execution_leases ("TenantId", "ExpiresAt");
            """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("DROP TABLE tenant_execution_leases;");
}
