using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// Application-layer tests verifying that the AllowNetworkEgress flag is threaded correctly
/// from CreateJob/UpdateJob commands through RunJob into WorkloadRunRequest.
/// Uses in-memory fakes — no I/O.
/// </summary>
public class NetworkEgressTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    private static CreateJobCommand ImageCmd(
        Guid projectId, string name,
        bool allowNetworkEgress = false) => new(
        ProjectId: projectId,
        Name: name,
        Description: null,
        MapImage: "img/worker:latest",
        MapRuntimeId: null, MapSource: null, MapEntrypoint: null,
        InputPayloads: new[] { "{}" },
        MapEnv: new Dictionary<string, string>(),
        ReduceImage: null,
        ReduceRuntimeId: null, ReduceSource: null, ReduceEntrypoint: null,
        ReduceEnv: null,
        ConcurrencyLimit: 1,
        SuccessExitCodes: new[] { 0 },
        PartialExitCodes: Array.Empty<int>(),
        AllowNetworkEgress: allowNetworkEgress);

    private static (RunJobHandler runH, InMemoryJobRepository jobs, FakeWorkloadRunner runner)
        BuildStack(bool egressDefault = false)
    {
        var jobs = new InMemoryJobRepository();
        var runs = new InMemoryJobRunRepository();
        var runner = new FakeWorkloadRunner();
        var uow = new RecordingUnitOfWork();
        var clock = new FakeClock(T0);
        return (new RunJobHandler(jobs, runs, runner, uow, clock), jobs, runner);
    }

    // ── CreateJob stores the flag ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateJob_AllowNetworkEgress_false_stored_in_view()
    {
        var jobs = new InMemoryJobRepository();
        var createH = new CreateJobHandler(jobs, new RecordingUnitOfWork(), new FakeClock(T0));

        var view = await createH.HandleAsync(ImageCmd(Guid.NewGuid(), "no-egress", allowNetworkEgress: false));

        Assert.False(view.AllowNetworkEgress);
    }

    [Fact]
    public async Task CreateJob_AllowNetworkEgress_true_stored_in_view()
    {
        var jobs = new InMemoryJobRepository();
        var createH = new CreateJobHandler(jobs, new RecordingUnitOfWork(), new FakeClock(T0));

        var view = await createH.HandleAsync(ImageCmd(Guid.NewGuid(), "with-egress", allowNetworkEgress: true));

        Assert.True(view.AllowNetworkEgress);
    }

    // ── UpdateJob preserves / changes the flag ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateJob_can_enable_egress_on_existing_job()
    {
        var jobs = new InMemoryJobRepository();
        var uow = new RecordingUnitOfWork();
        var clock = new FakeClock(T0);
        var createH = new CreateJobHandler(jobs, uow, clock);
        var updateH = new UpdateJobHandler(jobs, uow, clock);

        var created = await createH.HandleAsync(ImageCmd(Guid.NewGuid(), "originally-no-egress"));
        Assert.False(created.AllowNetworkEgress);

        var updateCmd = new UpdateJobCommand(
            JobId: created.Id,
            Name: "originally-no-egress",
            Description: null,
            MapImage: "img/worker:latest",
            MapRuntimeId: null, MapSource: null, MapEntrypoint: null,
            InputPayloads: new[] { "{}" },
            MapEnv: new Dictionary<string, string>(),
            ReduceImage: null,
            ReduceRuntimeId: null, ReduceSource: null, ReduceEntrypoint: null,
            ReduceEnv: null,
            ConcurrencyLimit: 1,
            SuccessExitCodes: new[] { 0 },
            PartialExitCodes: Array.Empty<int>(),
            AllowNetworkEgress: true);

        var updated = await updateH.HandleAsync(updateCmd);
        Assert.True(updated.AllowNetworkEgress);
    }

    // ── RunJob passes the flag into WorkloadRunRequest ────────────────────────────────────────────

    [Fact]
    public async Task RunJob_egress_false_passes_AllowNetworkEgress_false_to_runner()
    {
        var (runH, jobs, runner) = BuildStack();
        var createH = new CreateJobHandler(jobs, new RecordingUnitOfWork(), new FakeClock(T0));

        var job = await createH.HandleAsync(ImageCmd(Guid.NewGuid(), "egress-off", allowNetworkEgress: false));
        runner.EnqueueSuccessResults(1);

        await runH.HandleAsync(new RunJobCommand(job.Id));

        Assert.Single(runner.ReceivedRequests);
        Assert.False(runner.ReceivedRequests[0].AllowNetworkEgress);
    }

    [Fact]
    public async Task RunJob_egress_true_passes_AllowNetworkEgress_true_to_runner()
    {
        var (runH, jobs, runner) = BuildStack();
        var createH = new CreateJobHandler(jobs, new RecordingUnitOfWork(), new FakeClock(T0));

        var job = await createH.HandleAsync(ImageCmd(Guid.NewGuid(), "egress-on", allowNetworkEgress: true));
        runner.EnqueueSuccessResults(1);

        await runH.HandleAsync(new RunJobCommand(job.Id));

        Assert.Single(runner.ReceivedRequests);
        Assert.True(runner.ReceivedRequests[0].AllowNetworkEgress);
    }

    [Fact]
    public async Task RunJob_snapshot_records_egress_flag()
    {
        var (runH, jobs, runner) = BuildStack();
        var createH = new CreateJobHandler(jobs, new RecordingUnitOfWork(), new FakeClock(T0));

        var job = await createH.HandleAsync(ImageCmd(Guid.NewGuid(), "snap-egress", allowNetworkEgress: true));
        runner.EnqueueSuccessResults(1);

        var result = await runH.HandleAsync(new RunJobCommand(job.Id));

        Assert.True(result.Snapshot.AllowNetworkEgress);
    }
}
