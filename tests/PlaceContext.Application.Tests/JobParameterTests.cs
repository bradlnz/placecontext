using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// Tests job input parameters and the RunJob input override (modal-collected / event-injected payload):
/// the override replaces stored shard payloads with a single shard carrying the supplied payload.
/// </summary>
public class JobParameterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void JobParameter_rejects_empty_name()
        => Assert.Throws<ArgumentException>(() => new JobParameter("  "));

    [Fact]
    public void Job_with_parameters_requires_input()
    {
        var map = new MapSpec("img/worker:latest", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "j", null, map, null, 1, ExitCodePolicy.Default, T0,
            parameters: new[] { new JobParameter("customerEmail", "Customer email") });

        Assert.True(job.RequiresInput);
        Assert.Equal("customerEmail", job.Parameters[0].Name);
    }

    [Fact]
    public async Task RunJob_with_input_override_runs_single_shard_with_payload()
    {
        var jobs = new InMemoryJobRepository();
        var runs = new InMemoryJobRunRepository();
        var contexts = new InMemoryProjectContextRepository();
        var runner = new FakeWorkloadRunner();
        var uow = new RecordingUnitOfWork();
        var clock = new FakeClock(T0);

        // Job defined with two stored shards…
        var map = new MapSpec("img/worker:latest", new[] { "{\"a\":1}", "{\"a\":2}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "j", null, map, null, 1, ExitCodePolicy.Default, T0);
        await jobs.AddAsync(job);

        var handler = new RunJobHandler(jobs, runs, contexts, runner, uow, clock);

        // …but an override payload collapses the run to one shard carrying that payload.
        var detail = await handler.HandleAsync(new RunJobCommand(job.Id, "{\"customerEmail\":\"a@b.com\"}"));

        var request = Assert.Single(runner.ReceivedRequests);
        Assert.Equal("{\"customerEmail\":\"a@b.com\"}", request.StdinPayload);
        Assert.Single(detail.ShardResults);
        Assert.Equal(1, detail.Snapshot.ShardCount);
    }

    [Fact]
    public async Task RunJob_captures_named_artifacts_from_runner()
    {
        var jobs = new InMemoryJobRepository();
        var runs = new InMemoryJobRunRepository();
        var contexts = new InMemoryProjectContextRepository();
        var runner = new FakeWorkloadRunner();
        var uow = new RecordingUnitOfWork();
        var clock = new FakeClock(T0);

        // Runner returns result.json plus a named report.csv.
        runner.EnqueueResult(new PlaceContext.Application.Ports.WorkloadRunResult(
            0, "{}", "stdout", "",
            new[] { new PlaceContext.Application.Ports.WorkloadArtifact("report.csv", "a,b\n1,2") }));

        var map = new MapSpec("img/worker:latest", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "j", null, map, null, 1, ExitCodePolicy.Default, T0);
        await jobs.AddAsync(job);

        var handler = new RunJobHandler(jobs, runs, contexts, runner, uow, clock);
        var detail = await handler.HandleAsync(new RunJobCommand(job.Id));

        var artifact = Assert.Single(detail.ShardResults[0].Artifacts);
        Assert.Equal("report.csv", artifact.Name);
        Assert.Equal("a,b\n1,2", artifact.Content);
    }

    [Fact]
    public async Task RunJob_appends_the_run_summary_to_project_context()
    {
        var jobs = new InMemoryJobRepository();
        var runs = new InMemoryJobRunRepository();
        var contexts = new InMemoryProjectContextRepository();
        var runner = new FakeWorkloadRunner();
        var uow = new RecordingUnitOfWork();
        var clock = new FakeClock(T0);

        var map = new MapSpec("img/worker:latest", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "j", null, map, null, 1, ExitCodePolicy.Default, T0);
        await jobs.AddAsync(job);

        var handler = new RunJobHandler(jobs, runs, contexts, runner, uow, clock, events: null);
        await handler.HandleAsync(new RunJobCommand(job.Id));

        // The deterministic run summary is appended to the project context.
        var ctx = await contexts.GetForProjectAsync(
            PlaceContext.Domain.ValueObjects.ProjectId.From(job.ProjectId));
        Assert.NotNull(ctx);
        Assert.Contains("## Job run: j", ctx!.Markdown);
    }

    [Fact]
    public async Task RunJob_without_override_runs_stored_shards()
    {
        var jobs = new InMemoryJobRepository();
        var runs = new InMemoryJobRunRepository();
        var contexts = new InMemoryProjectContextRepository();
        var runner = new FakeWorkloadRunner();
        var uow = new RecordingUnitOfWork();
        var clock = new FakeClock(T0);

        var map = new MapSpec("img/worker:latest", new[] { "{\"a\":1}", "{\"a\":2}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "j", null, map, null, 2, ExitCodePolicy.Default, T0);
        await jobs.AddAsync(job);

        var handler = new RunJobHandler(jobs, runs, contexts, runner, uow, clock);
        var detail = await handler.HandleAsync(new RunJobCommand(job.Id));

        Assert.Equal(2, detail.ShardResults.Count);
    }
}
