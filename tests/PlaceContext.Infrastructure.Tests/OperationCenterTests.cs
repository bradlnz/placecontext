using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlaceContext.Application.Operations;
using PlaceContext.Application.Tenancy;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// OperationCenter correlation semantics: Sync upserts by (tenant, correlation key), a terminal
/// Sync seals the operation against late/wrong advisory Mark* calls, and the authoritative Sync
/// overrides an advisory terminal state that landed first.
/// </summary>
public class OperationCenterTests
{
    private static readonly TenantInfo Tenant = new(Guid.NewGuid(), "acme", "Acme", "UTC");

    private static OperationCenter Center() => new(new StubScopeFactory(), new StubLifetime().ApplicationStopping);

    [Fact]
    public void Private_build_operations_are_visible_only_to_the_submitting_user()
    {
        var center = Center();
        var user = Guid.NewGuid();
        var operation = center.Track(Tenant, null, "Private Build", "/build", userId: user);
        Assert.Empty(center.ListForTenant(Tenant.Id));
        Assert.Empty(center.ListForTenant(Tenant.Id, Guid.NewGuid()));
        Assert.Equal(0, center.ActiveCount(Tenant.Id));
        Assert.Equal(operation.Id, Assert.Single(center.ListForTenant(Tenant.Id, user)).Id);
        Assert.Equal(1, center.ActiveCount(Tenant.Id, user));
    }

    [Fact]
    public void Progress_only_wakes_the_matching_conversation_and_stops_after_disposal()
    {
        var center = Center();
        var user = Guid.NewGuid();
        var project = Guid.NewGuid();
        var fired = 0;
        var operation = center.Track(Tenant, project, "Assistant", null, "build:one", user);
        var subscription = center.Subscribe(Tenant.Id, () => fired++, user, project, "build:one");
        center.Track(Tenant, project, "Other conversation", null, "build:two", user);
        center.Track(Tenant, project, "Other user", null, "build:one", Guid.NewGuid());
        center.Track(Tenant, Guid.NewGuid(), "Other project", null, "build:one", user);
        center.Track(Tenant with
        {
            Id = Guid.NewGuid()
        }, project, "Other tenant", null, "build:one", user);
        Assert.Equal(0, fired);
        center.ReportProgress(operation.Id);
        Assert.Equal(1, fired);
        subscription.Dispose();
        center.ReportProgress(operation.Id);
        Assert.Equal(1, fired);
    }

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

    [Fact]
    public void Needs_input_remains_actionable_until_resume_and_converges_on_one_notification()
    {
        var center = Center();
        var project = Guid.NewGuid();
        var run = Guid.NewGuid();
        var chain = Guid.NewGuid();
        var key = $"chain-run:{run:N}";
        var deepLink = $"/project/{project}/workflows?chain={chain}&run={run}";
        var op = center.Track(Tenant, project, "Run workflow", "/observability", key);

        center.Sync(Tenant.Id, key, PortalOperationStatus.NeedsInput, "Run chain",
            deepLink, project, "Workflow paused — input required", DateTimeOffset.UtcNow, null);
        Assert.Equal(1, center.NeedsInputCount(Tenant.Id));
        Assert.Equal(0, center.ActiveCount(Tenant.Id));
        var paused = Assert.Single(center.ListForTenant(Tenant.Id));
        Assert.Equal(op.Id, paused.Id);
        Assert.Equal(deepLink, paused.Link);
        Assert.Equal(PortalOperationStatus.NeedsInput, paused.Status);
        center.MarkDone(op.Id, "workflow finished"); // stale advisory result from driver
        Assert.Equal(PortalOperationStatus.NeedsInput, paused.Status);

        center.Sync(Tenant.Id, key, PortalOperationStatus.NeedsInput, "Run chain",
            deepLink, project, null, DateTimeOffset.UtcNow, null);
        Assert.Single(center.ListForTenant(Tenant.Id));

        center.Sync(Tenant.Id, key, PortalOperationStatus.Running, "Run chain",
            "/observability?tab=workflows", project, "Resuming", DateTimeOffset.UtcNow, null);
        Assert.Equal(0, center.NeedsInputCount(Tenant.Id));
        Assert.Equal(1, center.ActiveCount(Tenant.Id));
        Assert.Equal("/observability?tab=workflows", paused.Link);

        center.Sync(Tenant.Id, key, PortalOperationStatus.Succeeded, "Run chain",
            "/observability?tab=workflows", project, "Completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        Assert.Equal(0, center.NeedsInputCount(Tenant.Id));
        Assert.Equal(0, center.ActiveCount(Tenant.Id));
        Assert.Single(center.ListForTenant(Tenant.Id));
    }

    [Fact]
    public void Needs_input_items_are_prioritised_above_routine_progress()
    {
        var center = Center();
        center.Sync(Tenant.Id, "chain:needs", PortalOperationStatus.NeedsInput, "Needs input",
            "/workflows", null, null, DateTimeOffset.UtcNow, null);
        center.Sync(Tenant.Id, "job:running", PortalOperationStatus.Running, "Running",
            "/jobs", null, null, DateTimeOffset.UtcNow, null);
        Assert.Equal(PortalOperationStatus.NeedsInput, center.ListForTenant(Tenant.Id)[0].Status);
    }

    // ── Retention ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sync_TrimsFinishedOperations_BeyondThePerTenantCap()
    {
        var center = Center();
        for (var i = 0; i < 60; i++)
        {
            center.Sync(Tenant.Id, $"job-run:{i}", PortalOperationStatus.Succeeded, $"Run job — {i}",
                        null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        }
        Assert.Equal(50, center.ListForTenant(Tenant.Id).Count);
    }

    [Fact]
    public void Changed_FiresOnSync()
    {
        var center = Center();
        var fired = 0;
        using var subscription = center.Subscribe(Tenant.Id, () => fired++);

        center.Sync(Tenant.Id, "job-run:abc", PortalOperationStatus.Running, "Run job — encode",
            null, null, null, DateTimeOffset.UtcNow, null);

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Ten_thousand_tenant_subscriptions_only_wake_the_changed_tenant()
    {
        var center = Center();
        var fired = 0;
        var subscriptions = Enumerable.Range(0, 10_000)
            .Select(_ => center.Subscribe(Guid.NewGuid(), () => fired++)).ToArray();
        using var target = center.Subscribe(Tenant.Id, () => fired++);
        center.Track(Tenant, null, "changed", null);
        Assert.Equal(1, fired);
        foreach (var subscription in subscriptions)
        {
            subscription.Dispose();
        }
    }

    [Fact]
    public void Subscriptions_filter_private_and_project_changes_and_stop_on_dispose()
    {
        var center = Center();
        var project = Guid.NewGuid();
        var user = Guid.NewGuid();
        var fired = 0;
        var subscription = center.Subscribe(Tenant.Id, () => fired++, user, project);
        center.Track(Tenant, Guid.NewGuid(), "Other project", null);
        center.Track(Tenant, project, "Other user", null, userId: Guid.NewGuid());
        Assert.Equal(0, fired);
        center.Track(Tenant, project, "Private", null, userId: user);
        center.Track(Tenant, project, "Shared", null);
        Assert.Equal(2, fired);
        subscription.Dispose();
        center.Track(Tenant, project, "After disposal", null);
        Assert.Equal(2, fired);
    }

    [Fact]
    public void Notifications_preserve_subscriber_context_and_isolate_throwing_subscribers()
    {
        var center = Center();
        var identity = new AsyncLocal<string>();
        identity.Value = "subscriber";
        string? observed = null;
        using var broken = center.Subscribe(Tenant.Id, () => throw new InvalidOperationException());
        using var healthy = center.Subscribe(Tenant.Id, () => observed = identity.Value);
        identity.Value = "publisher";
        var op = center.Track(Tenant, null, "Work", null);
        Assert.Equal("subscriber", observed);
        Assert.Equal("publisher", identity.Value);
        observed = null;
        center.MarkRunning(op.Id);
        Assert.Equal("subscriber", observed);
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
        public void StopApplication()
        {
        }
    }
}
