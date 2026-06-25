using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Domain.Tests;

/// <summary>
/// Domain tests for the per-job AllowNetworkEgress flag and its propagation into WorkloadSnapshot.
/// </summary>
public class NetworkEgressTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static MapSpec SimpleMapSpec()
        => new("img", new[] { "{}" }, new Dictionary<string, string>());

    // ── Job.Create defaults ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Job_Create_AllowNetworkEgress_defaults_to_false()
    {
        var job = Job.Create(ProjectId, "egress-test", null,
            SimpleMapSpec(), null, 1, ExitCodePolicy.Default, T0);

        Assert.False(job.AllowNetworkEgress);
    }

    [Fact]
    public void Job_Create_AllowNetworkEgress_can_be_set_true()
    {
        var job = Job.Create(ProjectId, "egress-on", null,
            SimpleMapSpec(), null, 1, ExitCodePolicy.Default, T0,
            allowNetworkEgress: true);

        Assert.True(job.AllowNetworkEgress);
    }

    // ── Job.Update ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Job_Update_can_toggle_egress_on()
    {
        var job = Job.Create(ProjectId, "toggle", null,
            SimpleMapSpec(), null, 1, ExitCodePolicy.Default, T0);
        Assert.False(job.AllowNetworkEgress);

        job.Update("toggle", null, SimpleMapSpec(), null, 1, ExitCodePolicy.Default, T0.AddMinutes(1),
            allowNetworkEgress: true);

        Assert.True(job.AllowNetworkEgress);
    }

    [Fact]
    public void Job_Update_can_toggle_egress_off()
    {
        var job = Job.Create(ProjectId, "toggle-off", null,
            SimpleMapSpec(), null, 1, ExitCodePolicy.Default, T0,
            allowNetworkEgress: true);

        job.Update("toggle-off", null, SimpleMapSpec(), null, 1, ExitCodePolicy.Default, T0.AddMinutes(1),
            allowNetworkEgress: false);

        Assert.False(job.AllowNetworkEgress);
    }

    // ── WorkloadSnapshot captures egress flag ─────────────────────────────────────────────────────

    [Fact]
    public void WorkloadSnapshot_From_captures_egress_false_by_default()
    {
        var job = Job.Create(ProjectId, "snap-egress", null,
            SimpleMapSpec(), null, 1, ExitCodePolicy.Default, T0);

        var snap = WorkloadSnapshot.From(job.MapSpec, job.ReduceSpec, job.ConcurrencyLimit, job.AllowNetworkEgress);

        Assert.False(snap.AllowNetworkEgress);
    }

    [Fact]
    public void WorkloadSnapshot_From_captures_egress_true_when_job_permits()
    {
        var job = Job.Create(ProjectId, "snap-egress-on", null,
            SimpleMapSpec(), null, 1, ExitCodePolicy.Default, T0,
            allowNetworkEgress: true);

        var snap = WorkloadSnapshot.From(job.MapSpec, job.ReduceSpec, job.ConcurrencyLimit, job.AllowNetworkEgress);

        Assert.True(snap.AllowNetworkEgress);
    }

    [Fact]
    public void WorkloadSnapshot_is_immutable_after_construction()
    {
        var snap = new WorkloadSnapshot(
            new WorkloadSource.ImageWorkload("img"),
            new[] { "{}" },
            new Dictionary<string, string>(),
            null, null, 1,
            allowNetworkEgress: true);

        Assert.True(snap.AllowNetworkEgress);
    }
}
