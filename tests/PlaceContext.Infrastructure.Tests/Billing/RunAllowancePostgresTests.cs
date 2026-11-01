using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Billing;
using Npgsql;
using PlaceContext.Infrastructure.Billing;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tests.AgentWorkflows;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests.Billing;

public sealed class RunAllowancePostgresTests
{
    [AgentPostgresFact]
    public async Task Unlimited_starts_are_idempotent_and_managed_throughput_is_paced()
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
                plan.IncludedRuns = 2; // A legacy cap may exist; it must not block unlimited starts.
                await setup.SaveChangesAsync();
            }

            // Hosted SaaS does not use the free self-hosted edition as a billing bypass.
            await using (var hostedDb = new AppDbContext(opts, tenant))
            {
                var hosted = new DbRunAllowance(hostedDb, tenant, clock,
                    Options.Create(new RunBillingDeploymentOptions { ManagedHosting = true }));
                Assert.Equal("self_hosted_only", (await hosted.GetAsync()).Status);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    hosted.ReserveAsync(projectId, Guid.NewGuid(), "job"));
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
            Assert.Equal(12, attempts.Count(x => x));
            await using (var verify = new AppDbContext(opts, tenant))
            {
                var state = await new DbRunAllowance(verify, tenant, clock).GetAsync();
                Assert.Equal(13L, state.UsedRuns);
                Assert.True(state.UnlimitedRuns);
                Assert.True(state.CanRun);
                Assert.Equal(13, await verify.BillableRuns.CountAsync());
            }
            var pacedClock = new FakeClock(DateTimeOffset.UtcNow);
            // Convert the same workspace to Basic: two concurrent starts must be
            // admitted rather than rejected, with ordered one-second start slots.
            await using (var setup = new AppDbContext(opts, tenant))
            {
                var plan = await setup.TenantRunPlans.SingleAsync();
                plan.Plan = RunPlans.Basic;
                plan.Status = "active";
                plan.PeriodStart = pacedClock.UtcNow.AddDays(-1);
                plan.PeriodEnd = pacedClock.UtcNow.AddDays(28);
                plan.NextDispatchAt = null;
                await setup.SaveChangesAsync();
            }

            // Anchor the admission clock immediately before starts: the setup transaction
            // may have taken time, and the production clock is not frozen.
            var admissionClock = new FakeClock(DateTimeOffset.UtcNow);
            var pacingElapsed = System.Diagnostics.Stopwatch.StartNew();
            await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var context = new AppDbContext(opts, tenant);
                await new DbRunAllowance(context, tenant, admissionClock).ReserveAsync(
                    projectId, Guid.NewGuid(), "workflow");
            }));
            await using (var verify = new AppDbContext(opts, tenant))
            {
                var plan = await verify.TenantRunPlans.SingleAsync();
                Assert.Equal(15L, plan.UsedRuns);
                Assert.Equal(RunPlans.Unlimited, plan.IncludedRuns);
                // PostgreSQL timestamptz stores microseconds, while .NET ticks are 100ns.
                // Allow a 10ms comparison tolerance without masking a missing start slot.
                Assert.True(plan.NextDispatchAt >= admissionClock.UtcNow.AddSeconds(2).AddMilliseconds(-10));
                Assert.True(pacingElapsed.Elapsed >= TimeSpan.FromMilliseconds(500),
                    "Managed starts should be paced rather than run immediately.");
                Assert.Equal(15, await verify.BillableRuns.CountAsync());
            }

            // A cancelled handler does not throw away its scheduled start slot. A
            // duplicate attempt must keep the original admission delay, and the
            // accounting ledger must still contain only one row for that run id.
            var retryRunId = Guid.NewGuid();
            await using (var setup = new AppDbContext(opts, tenant))
            {
                var plan = await setup.TenantRunPlans.SingleAsync();
                plan.NextDispatchAt = DateTimeOffset.UtcNow.AddSeconds(4);
                await setup.SaveChangesAsync();
            }
            await using (var cancelledAttempt = new AppDbContext(opts, tenant))
            {
                using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    new DbRunAllowance(cancelledAttempt, tenant,
                        new FakeClock(DateTimeOffset.UtcNow)).ReserveAsync(
                            projectId, retryRunId, "workflow", cancel.Token));
            }
            await using (var verify = new AppDbContext(opts, tenant))
            {
                var key = "workflow:" + retryRunId.ToString("N");
                var reservation = await verify.BillableRuns.SingleAsync(r => r.RunKey == key);
                Assert.NotNull(reservation.DispatchAfter);
                Assert.Equal(16L, (await verify.TenantRunPlans.SingleAsync()).UsedRuns);
                Assert.Equal(16, await verify.BillableRuns.CountAsync());
            }
            var retryElapsed = System.Diagnostics.Stopwatch.StartNew();
            await using (var retry = new AppDbContext(opts, tenant))
            {
                await new DbRunAllowance(retry, tenant,
                    new FakeClock(DateTimeOffset.UtcNow)).ReserveAsync(
                        projectId, retryRunId, "workflow");
            }
            Assert.True(retryElapsed.Elapsed >= TimeSpan.FromMilliseconds(500),
                "A duplicate run must not bypass its previously scheduled admission slot.");
            await using (var verify = new AppDbContext(opts, tenant))
            {
                Assert.Equal(16L, (await verify.TenantRunPlans.SingleAsync()).UsedRuns);
                Assert.Equal(16, await verify.BillableRuns.CountAsync());
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", conn);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
