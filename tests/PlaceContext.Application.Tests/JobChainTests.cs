using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

public class JobChainTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static RunJobChainHandler RunHandler(InMemoryJobChainRepository chains, InMemoryJobRepository jobs,
        IDispatcher dispatcher, InMemoryChainRunRepository? runs = null)
        => new(chains, jobs, runs ?? new InMemoryChainRunRepository(), new RecordingUnitOfWork(), new FakeClock(T0), dispatcher);

    private static Job MakeJob(string name, Guid? projectId = null)
    {
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        return Job.Create(projectId ?? ProjectId, name, null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0);
    }

    // ── definition ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_persists_the_chain_and_resolves_step_names()
    {
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("extract"); var b = MakeJob("report");
        await jobs.AddAsync(a); await jobs.AddAsync(b);
        var chains = new InMemoryJobChainRepository();
        var handler = new CreateJobChainHandler(chains, jobs, new RecordingUnitOfWork(), new FakeClock(T0));

        var view = await handler.HandleAsync(new CreateJobChainCommand(ProjectId, "nightly", "e2e", new[] { a.Id, b.Id }));

        Assert.Equal(new[] { "extract", "report" }, view.Steps.Select(s => s.JobName));
        Assert.NotNull(await chains.GetByIdAsync(view.Id));
    }

    [Fact]
    public async Task Create_rejects_unknown_jobs_and_jobs_from_other_projects()
    {
        var jobs = new InMemoryJobRepository();
        var foreign = MakeJob("other", Guid.NewGuid());
        await jobs.AddAsync(foreign);
        var handler = new CreateJobChainHandler(new InMemoryJobChainRepository(), jobs, new RecordingUnitOfWork(), new FakeClock(T0));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new CreateJobChainCommand(ProjectId, "c", null, new[] { Guid.NewGuid() })));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new CreateJobChainCommand(ProjectId, "c", null, new[] { foreign.Id })));
    }

    [Fact]
    public void Chain_requires_a_name_and_at_least_one_step()
    {
        Assert.Throws<ArgumentException>(() => JobChain.Create(ProjectId, " ", null, new[] { Guid.NewGuid() }, T0));
        Assert.Throws<ArgumentException>(() => JobChain.Create(ProjectId, "c", null, Array.Empty<Guid>(), T0));
    }

    // ── running ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_threads_each_steps_output_into_the_next_step()
    {
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("extract"); var b = MakeJob("report");
        await jobs.AddAsync(a); await jobs.AddAsync(b);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "pipeline", null, new[] { a.Id, b.Id }, T0);
        await chains.AddAsync(chain);

        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[a.Id] = Run(a.Id, "Succeeded", shardArtifacts: new[] { "{\"rows\":3}" });
        dispatcher.Results[b.Id] = Run(b.Id, "Succeeded", shardArtifacts: new[] { "{\"report\":\"done\"}" });

        var view = await RunHandler(chains, jobs, dispatcher)
            .HandleAsync(new RunJobChainCommand(chain.Id, "{\"from\":\"caller\"}"));

        Assert.Equal("Succeeded", view.Status);
        Assert.Equal(2, view.Steps.Count);
        // First step gets the caller's payload; second gets the first's artifact.
        Assert.Equal("{\"from\":\"caller\"}", dispatcher.Payloads[0]);
        Assert.Equal("{\"rows\":3}", dispatcher.Payloads[1]);
        // The chain's result is the last step's output.
        Assert.Equal("{\"report\":\"done\"}", view.FinalOutput);
    }

    [Fact]
    public async Task Run_stops_at_the_first_failed_step()
    {
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("a"); var b = MakeJob("b"); var c = MakeJob("c");
        await jobs.AddAsync(a); await jobs.AddAsync(b); await jobs.AddAsync(c);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "pipeline", null, new[] { a.Id, b.Id, c.Id }, T0);
        await chains.AddAsync(chain);

        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[a.Id] = Run(a.Id, "Succeeded", shardArtifacts: new[] { "{}" });
        dispatcher.Results[b.Id] = Run(b.Id, "Failed");

        var runs = new InMemoryChainRunRepository();
        var view = await RunHandler(chains, jobs, dispatcher, runs).HandleAsync(new RunJobChainCommand(chain.Id));

        Assert.Equal("Failed", view.Status);
        Assert.Equal(3, view.Steps.Count);                 // every stage is on the run...
        Assert.Equal("Failed", view.Steps[1].Status);
        Assert.Equal("Skipped", view.Steps[2].Status);     // ...steps after the failure are Skipped
        // The persisted progression is what the portal's live pipeline view observes.
        Assert.Contains("Running,Pending,Pending", runs.SavedStepSnapshots);
        Assert.Contains("Succeeded,Running,Pending", runs.SavedStepSnapshots);
        Assert.Equal("Succeeded,Failed,Skipped", runs.SavedStepSnapshots[^1]);
    }

    [Fact]
    public async Task Run_prefers_the_reduce_artifact_and_wraps_multiple_shards_as_a_json_array()
    {
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("map-reduce"); var b = MakeJob("fanout"); var c = MakeJob("sink");
        await jobs.AddAsync(a); await jobs.AddAsync(b); await jobs.AddAsync(c);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "pipeline", null, new[] { a.Id, b.Id, c.Id }, T0);
        await chains.AddAsync(chain);

        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[a.Id] = Run(a.Id, "Succeeded",
            shardArtifacts: new[] { "{\"ignored\":true}" }, reduceArtifact: "{\"total\":10}");
        dispatcher.Results[b.Id] = Run(b.Id, "Succeeded", shardArtifacts: new[] { "{\"n\":1}", "plain text" });
        dispatcher.Results[c.Id] = Run(c.Id, "Succeeded", shardArtifacts: new[] { "{}" });

        await RunHandler(chains, jobs, dispatcher).HandleAsync(new RunJobChainCommand(chain.Id));

        Assert.Equal("{\"total\":10}", dispatcher.Payloads[1]);              // reduce wins over shards
        Assert.Equal("[{\"n\":1},\"plain text\"]", dispatcher.Payloads[2]);  // shards → JSON array, text JSON-encoded
    }

    // ── fakes ───────────────────────────────────────────────────────────────────────────────────────

    private static JobRunDetailView Run(Guid jobId, string status,
        string[]? shardArtifacts = null, string? reduceArtifact = null)
    {
        var shards = (shardArtifacts ?? Array.Empty<string>())
            .Select((a, i) => new ShardResultView(i, 0, "Succeeded", a, null, Array.Empty<RunArtifactView>()))
            .ToList();
        var reduce = reduceArtifact is null
            ? null
            : new ReduceResultView(0, true, reduceArtifact, null, Array.Empty<RunArtifactView>());
        return new JobRunDetailView(Guid.NewGuid(), jobId, ProjectId, status, T0, T0.AddSeconds(1),
            shards, reduce, new JobRunSnapshotView("image", "img", null, null, 1, shards.Count, false));
    }

    /// <summary>Dispatcher that answers RunJobCommand from a canned per-job result and records the
    /// payload each step received — the seam that lets chain logic be tested without a runner.</summary>
    private sealed class FakeRunDispatcher : IDispatcher
    {
        public Dictionary<Guid, JobRunDetailView> Results { get; } = new();
        public List<string?> Payloads { get; } = new();

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            var run = (RunJobCommand)(object)command;
            Payloads.Add(run.InputPayload);
            return Task.FromResult((TResult)(object)Results[run.JobId]);
        }

        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
