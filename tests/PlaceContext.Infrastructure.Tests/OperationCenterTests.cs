using PlaceContext.Infrastructure.Operations;
using PlaceContext.Infrastructure.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// OperationCenter correlation semantics: Sync upserts by (tenant, correlation key), a terminal
/// Sync seals the operation against late/wrong advisory Mark* calls, and the authoritative Sync
/// overrides an advisory terminal state that landed first.
/// </summary>
public class OperationCenterTests
{
    private static readonly TenantInfo Tenant = new(Guid.NewGuid(), "acme", "Acme", "UTC");

    private static OperationCenter Center() => new(new StubScopeFactory(), new StubLifetime());

    // ── Sync upsert ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sync_CreatesAnOperation_WhenNothingTrackedTheWork()
    {
        var center = Center();

        center.Sync(Tenant.Id, "job-run:abc", PortalOperationStatus.Running, "Run job — encode",
            "/project/x/jobs", null, null, DateTimeOffset.UtcNow, null);

        var op = Assert.Single(center.ListForTenant(Tenant.Id));
        Assert.Equal(PortalOperationStatus.Running, op.Status);
        Assert.Equal("job-run:abc", op.CorrelationKey);
        Assert.Equal("Run job — encode", op.Title);
    }

    [Fact]
    public void Sync_ConvergesOnTheTrackedOperation_AndKeepsItsRicherTitle()
    {
        var center = Center();
        var tracked = center.Track(Tenant, null, "Run job — encode · nightly", null, "job-run:abc");

        center.Sync(Tenant.Id, "job-run:abc", PortalOperationStatus.Succeeded, "Run job — encode",
            null, null, "run finished — Succeeded", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var op = Assert.Single(center.ListForTenant(Tenant.Id));
        Assert.Equal(tracked.Id, op.Id);
        Assert.Equal(PortalOperationStatus.Succeeded, op.Status);
        Assert.Equal("Run job — encode · nightly", op.Title);
        Assert.Equal("run finished — Succeeded", op.Outcome);
    }

    [Fact]
    public void Sync_IsScopedToTheTenant()
    {
        var center = Center();
        var otherTenant = Guid.NewGuid();
        center.Track(Tenant, null, "Run job — encode", null, "job-run:abc");

        center.Sync(otherTenant, "job-run:abc", PortalOperationStatus.Failed, "Run job — encode",
            null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Assert.Equal(PortalOperationStatus.Queued, center.ListForTenant(Tenant.Id).Single().Status);
        Assert.Equal(PortalOperationStatus.Failed, center.ListForTenant(otherTenant).Single().Status);
    }

    // ── Sealing ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TerminalSync_SealsTheOperation_AgainstLateAdvisoryMarks()
    {
        var center = Center();
        var op = center.Track(Tenant, null, "Run job — encode", null, "job-run:abc");
        center.MarkRunning(op.Id);

        center.Sync(Tenant.Id, "job-run:abc", PortalOperationStatus.Failed, "Run job — encode",
            null, null, "run finished — Failed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        // The in-process wrapper finally returns (minutes later) and reports the wrong status.
        center.MarkDone(op.Id, "run finished — Succeeded");
        center.MarkRunning(op.Id);

        var final = center.ListForTenant(Tenant.Id).Single();
        Assert.Equal(PortalOperationStatus.Failed, final.Status);
        Assert.Equal("run finished — Failed", final.Outcome);
    }

    [Fact]
    public void AuthoritativeSync_Overrides_AnEarlierWrongAdvisoryTerminal()
    {
        var center = Center();
        var op = center.Track(Tenant, null, "Run job — encode", null, "job-run:abc");
        center.MarkDone(op.Id, "run finished — Failed"); // wrapper marked Done even for a failed run

        center.Sync(Tenant.Id, "job-run:abc", PortalOperationStatus.Failed, "Run job — encode",
            null, null, "run finished — Failed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Assert.Equal(PortalOperationStatus.Failed, center.ListForTenant(Tenant.Id).Single().Status);
    }

    [Fact]
    public void SyncOnASealedOperation_IsIgnored()
    {
        var center = Center();
        center.Sync(Tenant.Id, "job-run:abc", PortalOperationStatus.Succeeded, "Run job — encode",
            null, null, "run finished — Succeeded", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        // e.g. a restarted watcher replaying the same terminal row within its lookback window.
        center.Sync(Tenant.Id, "job-run:abc", PortalOperationStatus.Running, "Run job — encode",
            null, null, null, DateTimeOffset.UtcNow, null);

        Assert.Equal(PortalOperationStatus.Succeeded, center.ListForTenant(Tenant.Id).Single().Status);
    }

    [Fact]
    public void MarkRunning_NeverRegressesATerminalStatus()
    {
        var center = Center();
        var op = center.Track(Tenant, null, "import", null);
        center.MarkDone(op.Id, "done");

        center.MarkRunning(op.Id);

        Assert.Equal(PortalOperationStatus.Succeeded, center.ListForTenant(Tenant.Id).Single().Status);
    }

    // ── Retention ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sync_TrimsFinishedOperations_BeyondThePerTenantCap()
    {
        var center = Center();
        for (var i = 0; i < 60; i++)
            center.Sync(Tenant.Id, $"job-run:{i}", PortalOperationStatus.Succeeded, $"Run job — {i}",
                null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Assert.Equal(50, center.ListForTenant(Tenant.Id).Count);
    }

    [Fact]
    public void Changed_FiresOnSync()
    {
        var center = Center();
        var fired = 0;
        center.Changed += () => fired++;

        center.Sync(Tenant.Id, "job-run:abc", PortalOperationStatus.Running, "Run job — encode",
            null, null, null, DateTimeOffset.UtcNow, null);

        Assert.Equal(1, fired);
    }

    // ── Stubs (Sync/Track/Mark* never touch scopes or the lifetime) ─────────────────────────────

    private sealed class StubScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotSupportedException("not used in these tests");
    }

    private sealed class StubLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
