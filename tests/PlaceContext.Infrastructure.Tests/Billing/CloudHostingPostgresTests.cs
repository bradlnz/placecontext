using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using PlaceContext.Application.Access;
using PlaceContext.Application.Billing;
using PlaceContext.Infrastructure.Billing;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;
using PlaceContext.Infrastructure.Tests.AgentWorkflows;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests.Billing;

public sealed class CloudHostingPostgresTests
{
    private static readonly IOptions<RunBillingDeploymentOptions> Hosting = Options.Create(
        new RunBillingDeploymentOptions { ManagedHosting = true, BasicRunsPerPeriod = 2, BasicAiRequestsPerPeriod = 3 });

    [AgentPostgresFact]
    public async Task Concurrent_run_and_ai_reservations_stop_at_each_tenants_limit_and_replays_are_idempotent()
    {
        await using var store = await Store.CreateAsync();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var project = await store.SeedAsync(owner);
        await store.SeedAsync(other);
        var admitted = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            var id = Guid.NewGuid();
            await using var db = store.Open(owner);
            try
            {
                await Allowance(db, owner).ReserveAsync(project, id, "workflow");
                return (Guid?)id;
            }
            catch (TenantUsageLimitException) { return null; }
        }));
        Assert.Equal(2, admitted.Count(x => x is not null));
        await using (var db = store.Open(owner))
        {
            var allowance = Allowance(db, owner);
            // Reusing a durable reservation must not consume another slot or reject the last admitted run.
            await allowance.ReserveAsync(project, admitted.First(x => x is not null)!.Value, "workflow");
            var usage = await allowance.GetAsync();
            Assert.Equal(2, usage.UsedRuns);
            Assert.False(usage.UnlimitedRuns);
            Assert.False(usage.CanRun);
            Assert.True(usage.SubscriptionActive);
            Assert.Equal(2, await db.BillableRuns.CountAsync());
        }
        var ai = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var db = store.Open(owner);
            try { await Allowance(db, owner).ReserveAiRequestAsync(); return true; }
            catch (TenantUsageLimitException) { return false; }
        }));
        Assert.Equal(3, ai.Count(x => x));
        await using (var db = store.Open(other))
        {
            var allowance = Allowance(db, other);
            Assert.Equal(0, (await allowance.GetAsync()).UsedRuns);
            await allowance.ReserveAiRequestAsync();
            Assert.Equal(1, (await allowance.GetAsync()).UsedAiRequests);
        }
        using (var services = new ServiceCollection()
            .AddScoped(_ => store.Open(other))
            .AddScoped(sp => Allowance(sp.GetRequiredService<AppDbContext>(), other)).BuildServiceProvider())
        {
            var provider = new Provider();
            using var handler = new ManagedAiUsageHandler(services.GetRequiredService<IServiceScopeFactory>()) { InnerHandler = provider };
            using var client = new HttpClient(handler);
            for (var i = 0; i < 2; i++)
            {
                using var response = await client.PostAsync("https://model.example/test", new StringContent("{}"));
                Assert.Equal(System.Net.HttpStatusCode.BadGateway, response.StatusCode);
            }
            await Assert.ThrowsAsync<TenantUsageLimitException>(() => client.PostAsync("https://model.example/test", new StringContent("{}")));
            Assert.Equal(2, provider.Calls);
        }
        await using (var db = store.Open(owner))
        {
            var row = await db.TenantRunPlans.SingleAsync();
            row.Status = "inactive";
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<TenantUsageLimitException>(() => Allowance(db, owner).ReserveAiRequestAsync());
        }
        await using (var unresolved = store.Open(Guid.Empty))
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Allowance(unresolved, Guid.Empty).ReserveAiRequestAsync());
    }

    [AgentPostgresFact]
    public async Task Worker_claims_are_exclusive_recover_partial_setup_and_fence_expired_attempts()
    {
        await using var store = await Store.CreateAsync();
        var id = Guid.NewGuid();
        await store.SeedAsync(id);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var first = store.Open(id);
        var work = CloudWorkspaceProvisioner.ProcessOneAsync(first, async (tenant, plan, ct) =>
        {
            Assert.Equal(id, tenant);
            Assert.Equal(RunPlans.Basic, plan);
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            return "namespace";
        }, NullLogger.Instance, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using (var second = store.Open(id))
            Assert.False(await CloudWorkspaceProvisioner.ProcessOneAsync(second,
                (_, _, _) => throw new Exception("Duplicate claim"), NullLogger.Instance, default));
        release.SetResult();
        Assert.True(await work);
        await using (var check = store.Open(id))
        {
            Assert.Equal("ready", (await check.CloudWorkspaces.SingleAsync()).State);
            await check.CloudWorkspaces.ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "pending")
                .SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow));
            await CloudWorkspaceProvisioner.ProcessOneAsync(check,
                (_, _, _) => throw new InvalidOperationException("Temporary cluster failure with secret details"), NullLogger.Instance, default);
            var failed = await check.CloudWorkspaces.AsNoTracking().SingleAsync();
            Assert.Equal("failed", failed.State);
            Assert.Equal(2, failed.Attempts);
            Assert.Null(failed.LeaseToken);
            Assert.True(failed.NextAttemptAt > DateTimeOffset.UtcNow);
            await check.CloudWorkspaces.ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "provisioning")
                .SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
            Assert.True(await CloudWorkspaceProvisioner.ProcessOneAsync(check,
                (_, _, _) => Task.FromResult("recovered"), NullLogger.Instance, default));
            Assert.Equal("ready", (await check.CloudWorkspaces.AsNoTracking().SingleAsync()).State);
        }
        // A slow worker cannot mark a newer lease ready, even if its cluster operation succeeds.
        await using (var db = store.Open(id))
        {
            await db.CloudWorkspaces.ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "pending")
                .SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow));
            await CloudWorkspaceProvisioner.ProcessOneAsync(db, async (_, _, _) =>
            {
                await using var replacement = store.Open(id);
                await replacement.CloudWorkspaces.ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseToken, Guid.NewGuid()));
                return "late success";
            }, NullLogger.Instance, default);
            Assert.Equal("provisioning", (await db.CloudWorkspaces.AsNoTracking().SingleAsync()).State);
            await db.CloudWorkspaces.ExecuteUpdateAsync(s => s.SetProperty(x => x.Attempts, 5)
                .SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
            Assert.False(await CloudWorkspaceProvisioner.ProcessOneAsync(db,
                (_, _, _) => throw new Exception("Retry limit ignored"), NullLogger.Instance, default));
            Assert.Equal("failed", (await db.CloudWorkspaces.AsNoTracking().SingleAsync()).State);
        }
    }

    [AgentPostgresFact]
    public async Task Status_and_manual_retry_require_admin_and_stay_within_the_current_tenant()
    {
        await using var store = await Store.CreateAsync();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        await store.SeedAsync(owner);
        await store.SeedAsync(other);
        await using (var db = store.Open(owner))
        {
            await db.CloudWorkspaces.IgnoreQueryFilters().ExecuteUpdateAsync(s =>
                s.SetProperty(x => x.State, "failed").SetProperty(x => x.Attempts, 5));
        }
        using var services = new ServiceCollection().AddScoped(_ => store.Open(owner)).BuildServiceProvider();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        var workspace = new CloudWorkspace(factory, new FakeCurrentTenant(owner), new Permissions(true), Hosting);
        Assert.Equal("failed", (await workspace.GetAsync())!.State);
        await workspace.RetryAsync();
        Assert.Equal("pending", (await workspace.GetAsync())!.State);
        await using (var db = store.Open(other))
            Assert.Equal("failed", (await db.CloudWorkspaces.SingleAsync()).State);
        var forbidden = new CloudWorkspace(factory, new FakeCurrentTenant(owner), new Permissions(false), Hosting);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => forbidden.GetAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => forbidden.RetryAsync());
    }

    private static DbRunAllowance Allowance(AppDbContext db, Guid id) =>
        new(db, new FakeCurrentTenant(id), new FakeClock(DateTimeOffset.UtcNow), Hosting);

    private sealed class Provider : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway));
        }
    }

    private sealed class Permissions(bool allowed) : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(allowed);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid id, string roleName, CancellationToken ct = default) => GetEffectivePermissionsAsync(ct);
    }

    private sealed class Store(string schema, NpgsqlConnection connection, DbContextOptions<AppDbContext> options) : IAsyncDisposable
    {
        public AppDbContext Open(Guid tenant) => new(options, new FakeCurrentTenant(tenant));
        public static async Task<Store> CreateAsync()
        {
            var schema = "cloud_" + Guid.NewGuid().ToString("N");
            var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES")) { SearchPath = schema }.ConnectionString;
            var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", conn);
            await create.ExecuteNonQueryAsync();
            var store = new Store(schema, conn, new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options);
            await using var db = store.Open(Guid.Empty);
            await db.Database.MigrateAsync();
            return store;
        }
        public async Task<Guid> SeedAsync(Guid id)
        {
            await using var db = Open(id);
            var project = Guid.NewGuid();
            db.Tenants.Add(new TenantRow { Id = id, Slug = id.ToString("N"), Name = "Cloud test", CreatedAt = DateTimeOffset.UtcNow });
            db.Projects.Add(new ProjectRow { Id = project, Name = "Test" });
            await db.SaveChangesAsync();
            db.TenantRunPlans.Add(new TenantRunPlanRow { TenantId = id, Plan = RunPlans.Basic, Status = "active",
                PeriodStart = DateTimeOffset.UtcNow.AddDays(-1), PeriodEnd = DateTimeOffset.UtcNow.AddDays(29) });
            db.CloudWorkspaces.Add(new CloudWorkspaceRow { TenantId = id, NextAttemptAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return project;
        }
        public async ValueTask DisposeAsync()
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection);
            await drop.ExecuteNonQueryAsync();
            await connection.DisposeAsync();
        }
    }
}
