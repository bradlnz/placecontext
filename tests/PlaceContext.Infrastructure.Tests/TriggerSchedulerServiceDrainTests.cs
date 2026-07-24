using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Operations;
using PlaceContext.Infrastructure.Scheduling;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Infrastructure.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// Proves the drain batch executes claimed runs with BOUNDED PARALLELISM instead of the old serial
/// <c>foreach</c> — a run-queue throughput fix. Drives <see cref="TriggerSchedulerService.RunBatchInParallelAsync"/>
/// directly (an internal seam — see <see cref="InternalsVisibleTo"/> in AssemblyInfo.cs) so the test
/// needs no real Postgres: <c>ClaimAsync</c>/<c>DeleteManyAsync</c>'s raw-SQL plumbing is exercised
/// separately (manually, against the dev cluster) and isn't in scope for a unit test.
/// </summary>
public class TriggerSchedulerServiceDrainTests
{
    private static readonly TenantInfo Tenant = new(Guid.NewGuid(), "acme", "Acme", "UTC");
    private static IDataEncryptor TestEnc()
        => new DataProtectionEncryptor(new EphemeralDataProtectionProvider());

    [Fact]
    public async Task RunBatchInParallelAsync_RunsClaimedRunsConcurrently_NotSerially()
    {
        // A serial foreach could never satisfy a 3-party barrier — each dispatcher call would arrive
        // alone, time out waiting for the other two, and MaxObserved would top out at 1. Only genuine
        // overlap (>= drainParallelism runs in flight at once) lets every call reach the barrier.
        const int expectedConcurrency = 3;
        var dispatcher = new ConcurrencyProbeDispatcher(expectedConcurrency);

        var services = new ServiceCollection();
        services.AddScoped<IJobRepository>(_ => new FakeJobRepository());
        services.AddScoped<IDispatcher>(_ => dispatcher);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var opCenter = new OperationCenter(new StubScopeFactory(), new StubLifetime());
        var svc = new TriggerSchedulerService(scopeFactory, opCenter, TestEnc(),
            NullLogger<TriggerSchedulerService>.Instance, drainParallelism: expectedConcurrency);

        var tenantCache = new Dictionary<Guid, TenantInfo> { [Tenant.Id] = Tenant };
        var claimed = Enumerable.Range(0, expectedConcurrency)
            .Select(_ => new TriggerSchedulerService.ClaimedRun(Guid.NewGuid(), Tenant.Id, Guid.NewGuid(), Guid.NewGuid(), "nightly", null))
            .ToList();

        var runTask = svc.RunBatchInParallelAsync(claimed, tenantCache, CancellationToken.None);
        var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(ReferenceEquals(runTask, completed), "Drain batch did not complete in time — likely serialized again.");

        var processed = await runTask;
        Assert.Equal(expectedConcurrency, processed.Count);
        Assert.Equal(expectedConcurrency, dispatcher.MaxObserved);
    }

    [Fact]
    public async Task RunBatchInParallelAsync_CapsConcurrencyAtDrainParallelism()
    {
        // 6 claimed runs, only 2 allowed in flight at once — a 3rd concurrent arrival would deadlock
        // the 2-party barrier (proving the cap held) instead of completing, so bound the wait.
        const int drainParallelism = 2;
        var dispatcher = new ConcurrencyProbeDispatcher(drainParallelism);

        var services = new ServiceCollection();
        services.AddScoped<IJobRepository>(_ => new FakeJobRepository());
        services.AddScoped<IDispatcher>(_ => dispatcher);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var opCenter = new OperationCenter(new StubScopeFactory(), new StubLifetime());
        var svc = new TriggerSchedulerService(scopeFactory, opCenter, TestEnc(),
            NullLogger<TriggerSchedulerService>.Instance, drainParallelism: drainParallelism);

        var tenantCache = new Dictionary<Guid, TenantInfo> { [Tenant.Id] = Tenant };
        var claimed = Enumerable.Range(0, 6)
            .Select(_ => new TriggerSchedulerService.ClaimedRun(Guid.NewGuid(), Tenant.Id, Guid.NewGuid(), Guid.NewGuid(), "nightly", null))
            .ToList();

        var runTask = svc.RunBatchInParallelAsync(claimed, tenantCache, CancellationToken.None);
        var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.True(ReferenceEquals(runTask, completed), "Drain batch did not complete in time.");

        var processed = await runTask;
        Assert.Equal(6, processed.Count);
        Assert.Equal(drainParallelism, dispatcher.MaxObserved);
    }

    [Fact]
    public async Task RunBatchInParallelAsync_UsesCachedTenant_NeverHitsTheDbFallback()
    {
        var dispatcher = new ConcurrencyProbeDispatcher(1);
        var services = new ServiceCollection();
        services.AddScoped<IJobRepository>(_ => new FakeJobRepository());
        services.AddScoped<IDispatcher>(_ => dispatcher);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var opCenter = new OperationCenter(new StubScopeFactory(), new StubLifetime());
        // drainParallelism: 1 keeps this test's single claimed run trivially deterministic.
        var svc = new TriggerSchedulerService(scopeFactory, opCenter, TestEnc(),
            NullLogger<TriggerSchedulerService>.Instance, drainParallelism: 1);

        var tenantCache = new Dictionary<Guid, TenantInfo> { [Tenant.Id] = Tenant };
        var claimed = new List<TriggerSchedulerService.ClaimedRun>
        {
            new(Guid.NewGuid(), Tenant.Id, Guid.NewGuid(), Guid.NewGuid(), "nightly", null),
        };

        // FindTenantAsync (the DB fallback) needs a real AppDbContext/Postgres connection — it is never
        // registered in this test's DI container, so if the cache lookup were skipped this call would
        // throw resolving the service, not silently pass. A clean completion is the assertion.
        var processed = await svc.RunBatchInParallelAsync(claimed, tenantCache, CancellationToken.None);

        Assert.Single(processed);
        Assert.Equal(1, dispatcher.MaxObserved);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────────────────────────

    private sealed class ConcurrencyProbeDispatcher : IDispatcher
    {
        private readonly Barrier _barrier;
        private int _current;
        public int MaxObserved;

        public ConcurrencyProbeDispatcher(int expectedConcurrency) => _barrier = new Barrier(expectedConcurrency);

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            if (command is not RunJobCommand run)
                throw new NotSupportedException($"Unexpected command {command.GetType()}");

            var now = Interlocked.Increment(ref _current);
            InterlockedMax(ref MaxObserved, now);
            try
            {
                // Blocks until `expectedConcurrency` calls have arrived concurrently (or 5s elapses) —
                // proof of overlap, not just eventual completion.
                _barrier.SignalAndWait(TimeSpan.FromSeconds(5));
            }
            finally { Interlocked.Decrement(ref _current); }

            var snapshot = new JobRunSnapshotView("image", "img", null, null, 1, 1, false);
            var view = new JobRunDetailView(run.RunId ?? Guid.NewGuid(), run.JobId, Guid.NewGuid(), "Succeeded",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Array.Empty<ShardResultView>(), null, snapshot);
            return Task.FromResult((TResult)(object)view);
        }

        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by the drain path.");

        private static void InterlockedMax(ref int location, int value)
        {
            int initial;
            do
            {
                initial = location;
                if (value <= initial) return;
            }
            while (Interlocked.CompareExchange(ref location, value, initial) != initial);
        }
    }

    private sealed class FakeJobRepository : IJobRepository
    {
        public Task AddAsync(Job job, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Job job, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Job?> GetByIdAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult<Job?>(null);
        public Task<IReadOnlyList<Job>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Job>>(Array.Empty<Job>());
    }

    // OperationCenter's Track/MarkRunning/MarkDone/MarkFailed never touch the scope factory or the
    // lifetime — only its (unused-here) Run() helper does — so these stubs just need to exist.
    private sealed class StubScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotSupportedException("not used by Track/Mark*.");
    }

    private sealed class StubLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
