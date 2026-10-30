using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261030090000_IndexTenantRunHistory")]
public sealed class IndexTenantRunHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Reuse the indexes already installed by the legacy startup bootstrap.
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_job_runs_tenant_started ON job_runs (\"TenantId\", \"StartedAt\");");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_chain_runs_tenant_started ON chain_runs (\"TenantId\", \"StartedAt\");");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // These indexes predate the migration and are still used by the startup bootstrap.
    }
}
