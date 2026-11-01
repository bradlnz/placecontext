using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlaceContext.Infrastructure.Billing;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tests.AgentWorkflows;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests.Billing;

public sealed class RunAllowancePostgresTests
{
    [AgentPostgresFact]
    public async Task Concurrent_run_starts_cannot_exceed_the_plan_and_duplicate_ids_charge_once()
    {
        var schema = "quota_test_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES")) { SearchPath = schema }.ConnectionString;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", conn))
            await create.ExecuteNonQueryAsync();
        try
        {
            var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
            var tenantId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var tenant = new FakeCurrentTenant(tenantId);
            var clock = new FakeClock(DateTimeOffset.UtcNow);
            await using (var setup = new AppDbContext(opts, tenant))
            {
                await setup.Database.MigrateAsync();
                setup.Tenants.Add(new TenantRow { Id = tenantId, Slug = schema, Name = schema, CreatedAt = clock.UtcNow });
                setup.Projects.Add(new ProjectRow { Id = projectId, Name = "Quota test" });
                await setup.SaveChangesAsync();
            }

            var runId = Guid.NewGuid();
            await using (var first = new AppDbContext(opts, tenant))
            {
                var allowance = new DbRunAllowance(first, tenant, clock);
                await allowance.ReserveAsync(projectId, runId, "workflow");
                await allowance.ReserveAsync(projectId, runId, "workflow");
            }

            await using (var setup = new AppDbContext(opts, tenant))
            {
                var plan = await setup.TenantRunPlans.SingleAsync();
                Assert.Equal(1, plan.UsedRuns);
                plan.IncludedRuns = 2;
                await setup.SaveChangesAsync();
            }

            var attempts = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
            {
                await using var context = new AppDbContext(opts, tenant);
                try
                {
                    await new DbRunAllowance(context, tenant, clock).ReserveAsync(
                        projectId, Guid.NewGuid(), "job");
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            }));
            Assert.Equal(1, attempts.Count(x => x));
            await using (var verify = new AppDbContext(opts, tenant))
            {
                var state = await new DbRunAllowance(verify, tenant, clock).GetAsync();
                Assert.Equal(2, state.UsedRuns);
                Assert.Equal(0, state.Remaining);
                Assert.Equal(2, await verify.BillableRuns.CountAsync());
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", conn);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
