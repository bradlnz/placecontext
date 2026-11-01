using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using PlaceContext.Application.Billing;
using PlaceContext.Infrastructure.Billing;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tests.AgentWorkflows;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests.Billing;

public sealed class ExecutionCapacityPostgresTests
{
    [AgentPostgresFact]
    public async Task Basic_capacity_is_global_and_waits_instead_of_rejecting_excess_runs()
    {
        var schema = "capacity_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES")) { SearchPath = schema }.ConnectionString;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", conn))
            await cmd.ExecuteNonQueryAsync();
        try
        {
            var tenantId = Guid.NewGuid();
            var owner = new FakeCurrentTenant(tenantId);
            var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
            await using (var db = new AppDbContext(opts, owner))
            {
                await db.Database.MigrateAsync();
                db.Tenants.Add(new TenantRow { Id = tenantId, Slug = schema, Name = schema,
                    CreatedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
                db.TenantRunPlans.Add(new TenantRunPlanRow {
                    TenantId = tenantId, Plan = RunPlans.Basic, Status = "active",
                    PeriodStart = DateTimeOffset.UtcNow.AddHours(-1),
                    PeriodEnd = DateTimeOffset.UtcNow.AddDays(7)
                });
                await db.SaveChangesAsync();
            }
            async Task<IExecutionLease> Acquire(Guid id, CancellationToken ct = default)
            {
                await using var context = new AppDbContext(opts, owner);
                return await new DbExecutionCapacity(context, owner,
                    Options.Create(new RunBillingDeploymentOptions { ManagedHosting = true }),
                    Options.Create(new StripeRunBillingOptions()),
                    NullLogger<DbExecutionCapacity>.Instance)
                    .AcquireAsync(id, "workflow", ct);
            }
            var first = await Acquire(Guid.NewGuid());
            await using var second = await Acquire(Guid.NewGuid());
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Acquire(Guid.NewGuid(), cancel.Token));
            await first.DisposeAsync();
            await using var replacement = await Acquire(Guid.NewGuid());
            await using (var verify = new AppDbContext(opts, owner))
                Assert.Equal(2L, await verify.Database.SqlQueryRaw<long>(
                    "SELECT COUNT(*) AS \"Value\" FROM tenant_execution_leases").SingleAsync());
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", conn);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
