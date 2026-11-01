using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261101010000_UnlimitedRunsAndAdmissionPacing")]
public sealed class UnlimitedRunsAndAdmissionPacing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE tenant_run_plans
                ADD COLUMN "NextDispatchAt" timestamptz NULL;
            ALTER TABLE tenant_run_plans
                ALTER COLUMN "UsedRuns" TYPE bigint USING "UsedRuns"::bigint;
            ALTER TABLE tenant_run_plans
                ALTER COLUMN "IncludedRuns" SET DEFAULT 0;
            UPDATE tenant_run_plans SET "IncludedRuns" = 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM tenant_run_plans WHERE "UsedRuns" > 2147483647) THEN
                    RAISE EXCEPTION 'Cannot down-migrate usage greater than int32 range';
                END IF;
            END $$;
            ALTER TABLE tenant_run_plans DROP COLUMN "NextDispatchAt";
            ALTER TABLE tenant_run_plans
                ALTER COLUMN "UsedRuns" TYPE integer USING "UsedRuns"::integer;
            ALTER TABLE tenant_run_plans
                ALTER COLUMN "IncludedRuns" SET DEFAULT 100;
            """);
    }
}
