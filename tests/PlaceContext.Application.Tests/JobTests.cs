using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// Application-layer tests for the Jobs feature: CreateJob, UpdateJob, RunJob (fan-out, concurrency,
/// optional reduce, status mapping via policy, code workloads), and queries.
/// All are unit tests using in-memory fakes.
/// </summary>
public class JobTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Image-based CreateJobCommand (existing behaviour).</summary>
    private static CreateJobCommand ImageCmd(
        Guid projectId, string name,
        string mapImage = "img/worker:latest",
        string[]? payloads = null,
        string? reduceImage = null,
        int concurrency = 1,
        int[]? successCodes = null,
        int[]? partialCodes = null) => new(
        ProjectId: projectId,
        Name: name,
        Description: null,
        MapImage: mapImage,
        MapRuntimeId: null,
        MapSource: null,
        MapEntrypoint: null,
        InputPayloads: payloads ?? new[] { "{}" },
        MapEnv: new Dictionary<string, string>(),
        ReduceImage: reduceImage,
        ReduceRuntimeId: null,
        ReduceSource: null,
        ReduceEntrypoint: null,
        ReduceEnv: null,
        ConcurrencyLimit: concurrency,
        SuccessExitCodes: successCodes ?? new[] { 0 },
        PartialExitCodes: partialCodes ?? Array.Empty<int>());

    /// <summary>Code-based CreateJobCommand.</summary>
    private static CreateJobCommand CodeCmd(
        Guid projectId, string name,
        string runtimeId, string source,
        string[]? payloads = null,
        int concurrency = 1) => new(
        ProjectId: projectId,
        Name: name,
        Description: null,
        MapImage: null,
        MapRuntimeId: runtimeId,
        MapSource: source,
        MapEntrypoint: null,
        InputPayloads: payloads ?? new[] { "{}" },
        MapEnv: new Dictionary<string, string>(),
        ReduceImage: null,
        ReduceRuntimeId: null,
        ReduceSource: null,
        ReduceEntrypoint: null,
        ReduceEnv: null,
        ConcurrencyLimit: concurrency,
        SuccessExitCodes: new[] { 0 },
        PartialExitCodes: Array.Empty<int>());

    private static WorkloadSnapshot DefaultSnapshot()
    {
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        return WorkloadSnapshot.From(mapSpec, null, 1);
    }

    private static (CreateJobHandler handler, InMemoryJobRepository jobs) BuildCreateHandler()
    {
        var jobs = new InMemoryJobRepository();
        var uow = new RecordingUnitOfWork();
        var clock = new FakeClock(T0);
        return (new CreateJobHandler(jobs, uow, clock), jobs);
    }

    private static (RunJobHandler handler, InMemoryJobRepository jobs, InMemoryJobRunRepository runs,
        FakeWorkloadRunner runner)
        BuildRunHandler()
    {
        var jobs = new InMemoryJobRepository();
        var runs = new InMemoryJobRunRepository();
        var runner = new FakeWorkloadRunner();
        var uow = new RecordingUnitOfWork();
        var clock = new FakeClock(T0);
        return (new RunJobHandler(jobs, runs, runner, uow, clock), jobs, runs, runner);
    }

    // ── CreateJob ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateJob_persists_job_with_correct_shard_count()
    {
        var (handler, jobs) = BuildCreateHandler();
        var projectId = Guid.NewGuid();
        var cmd = new CreateJobCommand(
            ProjectId: projectId,
            Name: "My Job",
            Description: "does stuff",
            MapImage: "img/worker:latest",
            MapRuntimeId: null,
            MapSource: null,
            MapEntrypoint: null,
            InputPayloads: new[] { @"{""a"":1}", @"{""a"":2}", @"{""a"":3}" },
            MapEnv: new Dictionary<string, string> { ["KEY"] = "val" },
            ReduceImage: null,
            ReduceRuntimeId: null,
            ReduceSource: null,
            ReduceEntrypoint: null,
            ReduceEnv: null,
            ConcurrencyLimit: 2,
            SuccessExitCodes: new[] { 0 },
            PartialExitCodes: Array.Empty<int>());

        var view = await handler.HandleAsync(cmd);

        Assert.Equal("My Job", view.Name);
        Assert.Equal(3, view.ShardCount);
        Assert.Equal("image", view.MapSourceKind);
        Assert.Equal("img/worker:latest", view.MapImage);
        Assert.Null(view.ReduceImage);
        Assert.Equal(2, view.ConcurrencyLimit);
        Assert.Single(await jobs.ListForProjectAsync(cmd.ProjectId));
    }

    [Fact]
    public async Task CreateJob_code_workload_stores_runtimeId_and_source()
    {
        var (handler, _) = BuildCreateHandler();
        var cmd = CodeCmd(Guid.NewGuid(), "Code Job", "node", "process.exit(0);");
        var view = await handler.HandleAsync(cmd);

        Assert.Equal("code", view.MapSourceKind);
        Assert.Equal("node", view.MapRuntimeId);
        Assert.Equal("process.exit(0);", view.MapSource);
        Assert.Null(view.MapImage);
    }

    [Fact]
    public async Task CreateJob_with_reduce_spec_persists_reduce_image()
    {
        var (handler, _) = BuildCreateHandler();
        var cmd = new CreateJobCommand(
            ProjectId: Guid.NewGuid(),
            Name: "Map+Reduce",
            Description: null,
            MapImage: "img/map:latest",
            MapRuntimeId: null,
            MapSource: null,
            MapEntrypoint: null,
            InputPayloads: new[] { "{}" },
            MapEnv: new Dictionary<string, string>(),
            ReduceImage: "img/reduce:latest",
            ReduceRuntimeId: null,
            ReduceSource: null,
            ReduceEntrypoint: null,
            ReduceEnv: new Dictionary<string, string>(),
            ConcurrencyLimit: 1,
            SuccessExitCodes: new[] { 0 },
            PartialExitCodes: new[] { 3 });

        var view = await handler.HandleAsync(cmd);

        Assert.Equal("img/reduce:latest", view.ReduceImage);
        Assert.Equal("image", view.ReduceSourceKind);
    }

    // ── UpdateJob ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateJob_changes_name_and_switches_to_code_workload()
    {
        var (createH, jobs) = BuildCreateHandler();
        var uow = new RecordingUnitOfWork();
        var clock = new FakeClock(T0);
        var updateH = new UpdateJobHandler(jobs, uow, clock);

        var projectId = Guid.NewGuid();
        var created = await createH.HandleAsync(ImageCmd(projectId, "Original"));

        var updateCmd = new UpdateJobCommand(
            JobId: created.Id,
            Name: "Renamed",
            Description: "switched to code",
            MapImage: null,
            MapRuntimeId: "python",
            MapSource: "print('hello')",
            MapEntrypoint: "main.py",
            InputPayloads: new[] { "{}", "{}" },
            MapEnv: new Dictionary<string, string>(),
            ReduceImage: null,
            ReduceRuntimeId: null,
            ReduceSource: null,
            ReduceEntrypoint: null,
            ReduceEnv: null,
            ConcurrencyLimit: 2,
            SuccessExitCodes: new[] { 0 },
            PartialExitCodes: Array.Empty<int>());

        var view = await updateH.HandleAsync(updateCmd);

        Assert.Equal("Renamed", view.Name);
        Assert.Equal("code", view.MapSourceKind);
        Assert.Equal("python", view.MapRuntimeId);
        Assert.Equal("print('hello')", view.MapSource);
        Assert.Equal("main.py", view.MapEntrypoint);
        Assert.Equal(2, view.ShardCount);
        Assert.Equal(2, view.ConcurrencyLimit);
    }

    [Fact]
    public async Task UpdateJob_throws_when_job_not_found()
    {
        var jobs = new InMemoryJobRepository();
        var updateH = new UpdateJobHandler(jobs, new RecordingUnitOfWork(), new FakeClock(T0));

        var cmd = new UpdateJobCommand(
            JobId: Guid.NewGuid(),
            Name: "X", Description: null,
            MapImage: "img", MapRuntimeId: null, MapSource: null, MapEntrypoint: null,
            InputPayloads: new[] { "{}" }, MapEnv: new Dictionary<string, string>(),
            ReduceImage: null, ReduceRuntimeId: null, ReduceSource: null, ReduceEntrypoint: null,
            ReduceEnv: null, ConcurrencyLimit: 1,
            SuccessExitCodes: new[] { 0 }, PartialExitCodes: Array.Empty<int>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => updateH.HandleAsync(cmd));
    }

    // ── RunJob: fan-out ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunJob_invokes_one_container_per_shard()
    {
        var (runHandler, jobs, _, runner) = BuildRunHandler();
        var createH = new CreateJobHandler(jobs, new RecordingUnitOfWork(), new FakeClock(T0));

        var projectId = Guid.NewGuid();
        var job = await createH.HandleAsync(ImageCmd(
            projectId, "Fan-out test",
            payloads: new[] { @"{""i"":0}", @"{""i"":1}", @"{""i"":2}" },
            concurrency: 3));

        runner.EnqueueSuccessResults(3);

        var runResult = await runHandler.HandleAsync(new RunJobCommand(job.Id));

        Assert.Equal(3, runner.ReceivedRequests.Count);
        Assert.Equal("Succeeded", runResult.Status);
        Assert.Equal(3, runResult.ShardResults.Count);
    }

    [Fact]
    public async Task RunJob_honors_a_preallocated_run_id()
    {
        var (runHandler, jobs, runs, runner) = BuildRunHandler();
        var createH = new CreateJobHandler(jobs, new RecordingUnitOfWork(), new FakeClock(T0));
        var job = await createH.HandleAsync(ImageCmd(Guid.NewGuid(), "correlated"));
        runner.EnqueueSuccessResults(1);

        var runId = Guid.NewGuid();
        var runResult = await runHandler.HandleAsync(new RunJobCommand(job.Id, null, runId));

        // The caller's id names the run, so its tracking (bell op, chain step) correlates
        // with the persisted row before the handler returns.
        Assert.Equal(runId, runResult.Id);
        Assert.NotNull(await runs.GetByIdAsync(runId));
    }

    [Fact]
    public async Task RunJob_uses_correct_image_and_payload_for_each_shard()
    {
        var (runHandler, jobRepo, _, runner) = BuildRunHandler();
        var createH = new CreateJobHandler(jobRepo, new RecordingUnitOfWork(), new FakeClock(T0));

        var payloads = new[] { @"{""shard"":0}", @"{""shard"":1}" };
        var job = await createH.HandleAsync(new CreateJobCommand(
            ProjectId: Guid.NewGuid(), Name: "payload-check", Description: null,
            MapImage: "my/img:v1", MapRuntimeId: null, MapSource: null, MapEntrypoint: null,
            InputPayloads: payloads,
            MapEnv: new Dictionary<string, string> { ["MYKEY"] = "myval" },
            ReduceImage: null, ReduceRuntimeId: null, ReduceSource: null, ReduceEntrypoint: null,
            ReduceEnv: null, ConcurrencyLimit: 2,
            SuccessExitCodes: new[] { 0 }, PartialExitCodes: Array.Empty<int>()));

        runner.EnqueueSuccessResults(2);
        await runHandler.HandleAsync(new RunJobCommand(job.Id));

        var sentImages = runner.ReceivedRequests.Select(r => r.Image).Distinct().ToList();
        Assert.Single(sentImages);
        Assert.Equal("my/img:v1", sentImages[0]);

        var sentPayloads = runner.ReceivedRequests.Select(r => r.StdinPayload).OrderBy(p => p).ToList();
        Assert.Equal(payloads.OrderBy(p => p).ToList(), sentPayloads);

        Assert.All(runner.ReceivedRequests, r =>
            Assert.True(r.Env.ContainsKey("MYKEY")));
    }

    [Fact]
    public async Task RunJob_code_workload_sends_runtimeId_and_source_to_runner()
    {
        var (runHandler, jobRepo, _, runner) = BuildRunHandler();
        var createH = new CreateJobHandler(jobRepo, new RecordingUnitOfWork(), new FakeClock(T0));

        var job = await createH.HandleAsync(CodeCmd(
            Guid.NewGuid(), "code-fanout", "node", "process.exit(0);",
            payloads: new[] { "{}", "{}" }, concurrency: 2));

        runner.EnqueueSuccessResults(2);
        await runHandler.HandleAsync(new RunJobCommand(job.Id));

        Assert.Equal(2, runner.ReceivedRequests.Count);
        Assert.All(runner.ReceivedRequests, r =>
        {
            Assert.Null(r.Image);              // no image for code workload
            Assert.Equal("node", r.RuntimeId);
            Assert.NotNull(r.CodeFiles);
            Assert.Equal("process.exit(0);", r.CodeFiles!.Single().Content);
        });
    }

    // ── RunJob: status aggregation via policy ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunJob_any_failed_shard_yields_Failed_status()
    {
        var (runHandler, jobRepo, runs, runner) = BuildRunHandler();
        var createH = new CreateJobHandler(jobRepo, new RecordingUnitOfWork(), new FakeClock(T0));

        var job = await createH.HandleAsync(ImageCmd(Guid.NewGuid(), "fail-test",
            payloads: new[] { "{}", "{}" }));

        runner.EnqueueSuccessResults(1);
        runner.EnqueueFailedResult();

        var result = await runHandler.HandleAsync(new RunJobCommand(job.Id));

        Assert.Equal("Failed", result.Status);
        Assert.Contains(result.ShardResults, s => s.Outcome == "Failed");
    }

    [Fact]
    public async Task RunJob_partial_exit_code_yields_Partial_status_with_custom_policy()
    {
        var (runHandler, jobRepo, _, runner) = BuildRunHandler();
        var createH = new CreateJobHandler(jobRepo, new RecordingUnitOfWork(), new FakeClock(T0));

        var job = await createH.HandleAsync(ImageCmd(Guid.NewGuid(), "partial-test",
            payloads: new[] { "{}", "{}" },
            successCodes: new[] { 0 }, partialCodes: new[] { 3 }));

        runner.EnqueueSuccessResults(1);
        runner.EnqueuePartialResult(3);

        var result = await runHandler.HandleAsync(new RunJobCommand(job.Id));

        Assert.Equal("Partial", result.Status);
    }

    // ── RunJob: optional reduce ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunJob_calls_reduce_container_when_spec_present()
    {
        var (runHandler, jobRepo, _, runner) = BuildRunHandler();
        var createH = new CreateJobHandler(jobRepo, new RecordingUnitOfWork(), new FakeClock(T0));

        var job = await createH.HandleAsync(new CreateJobCommand(
            ProjectId: Guid.NewGuid(), Name: "with-reduce", Description: null,
            MapImage: "img/map", MapRuntimeId: null, MapSource: null, MapEntrypoint: null,
            InputPayloads: new[] { "{}", "{}" },
            MapEnv: new Dictionary<string, string>(),
            ReduceImage: "img/reduce",
            ReduceRuntimeId: null, ReduceSource: null, ReduceEntrypoint: null,
            ReduceEnv: new Dictionary<string, string> { ["REDUCE_KEY"] = "rv" },
            ConcurrencyLimit: 2,
            SuccessExitCodes: new[] { 0 }, PartialExitCodes: Array.Empty<int>()));

        runner.EnqueueSuccessResults(2);
        runner.EnqueueResult(new WorkloadRunResult(0, @"{""reduced"":true}", "reduce stdout", ""));

        var result = await runHandler.HandleAsync(new RunJobCommand(job.Id));

        // 2 map + 1 reduce = 3 total container invocations
        Assert.Equal(3, runner.ReceivedRequests.Count);
        Assert.NotNull(result.ReduceResult);
        Assert.True(result.ReduceResult!.Succeeded);
        Assert.Equal(@"{""reduced"":true}", result.ReduceResult.Artifact);
        var reduceReq = runner.ReceivedRequests.Last();
        Assert.Equal("img/reduce", reduceReq.Image);
    }

    [Fact]
    public async Task RunJob_does_not_call_reduce_when_no_reduce_spec()
    {
        var (runHandler, jobRepo, _, runner) = BuildRunHandler();
        var createH = new CreateJobHandler(jobRepo, new RecordingUnitOfWork(), new FakeClock(T0));

        var job = await createH.HandleAsync(ImageCmd(Guid.NewGuid(), "no-reduce"));

        runner.EnqueueSuccessResults(1);

        var result = await runHandler.HandleAsync(new RunJobCommand(job.Id));

        Assert.Equal(1, runner.ReceivedRequests.Count);
        Assert.Null(result.ReduceResult);
    }

    // ── RunJob: snapshot on the run ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunJob_snapshot_captures_workload_source_at_run_time()
    {
        var (runHandler, jobRepo, runs, runner) = BuildRunHandler();
        var createH = new CreateJobHandler(jobRepo, new RecordingUnitOfWork(), new FakeClock(T0));

        var job = await createH.HandleAsync(ImageCmd(Guid.NewGuid(), "snap-test",
            mapImage: "img/v1", payloads: new[] { "{}", "{}" }, concurrency: 2));

        runner.EnqueueSuccessResults(2);
        var result = await runHandler.HandleAsync(new RunJobCommand(job.Id));

        Assert.NotNull(result.Snapshot);
        Assert.Equal("image", result.Snapshot.MapSourceKind);
        Assert.Equal("img/v1", result.Snapshot.MapSourceLabel);
        Assert.Equal(2, result.Snapshot.ConcurrencyLimit);
        Assert.Equal(2, result.Snapshot.ShardCount);
    }

    [Fact]
    public async Task RunJob_snapshot_is_code_kind_for_code_workload()
    {
        var (runHandler, jobRepo, _, runner) = BuildRunHandler();
        var createH = new CreateJobHandler(jobRepo, new RecordingUnitOfWork(), new FakeClock(T0));

        var job = await createH.HandleAsync(CodeCmd(Guid.NewGuid(), "code-snap", "python", "print('hi')"));

        runner.EnqueueSuccessResults(1);
        var result = await runHandler.HandleAsync(new RunJobCommand(job.Id));

        Assert.Equal("code", result.Snapshot.MapSourceKind);
        Assert.Equal("python", result.Snapshot.MapSourceLabel);
    }

    // ── RunJob: project scoping ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunJob_stores_run_scoped_to_correct_project()
    {
        var (runHandler, jobRepo, runs, runner) = BuildRunHandler();
        var createH = new CreateJobHandler(jobRepo, new RecordingUnitOfWork(), new FakeClock(T0));
        var projectId = Guid.NewGuid();

        var job = await createH.HandleAsync(ImageCmd(projectId, "scoping-test"));

        runner.EnqueueSuccessResults(1);
        var result = await runHandler.HandleAsync(new RunJobCommand(job.Id));

        Assert.Equal(projectId, result.ProjectId);
        var storedRun = await runs.GetByIdAsync(result.Id);
        Assert.NotNull(storedRun);
        Assert.Equal(projectId, storedRun!.ProjectId);
    }

    // ── Queries ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListJobs_returns_jobs_for_project()
    {
        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var jobRepo = new InMemoryJobRepository();
        var createH = new CreateJobHandler(jobRepo, new RecordingUnitOfWork(), new FakeClock(T0));

        await createH.HandleAsync(ImageCmd(projectId, "Job A"));
        await createH.HandleAsync(ImageCmd(projectId, "Job B"));
        await createH.HandleAsync(ImageCmd(otherProjectId, "Other Job"));

        var handler = new ListJobsHandler(jobRepo);
        var result = await handler.HandleAsync(new ListJobsQuery(projectId));

        Assert.Equal(2, result.Count);
        Assert.All(result, j => Assert.Equal(projectId, j.ProjectId));
    }

    [Fact]
    public async Task ListJobRuns_returns_runs_for_job_descending()
    {
        var runRepo = new InMemoryJobRunRepository();
        var jobId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var snap = DefaultSnapshot();

        var run1 = JobRun.Start(jobId, projectId, T0, snap);
        run1.Complete(new[] { new ShardResult(0, 0, WorkloadOutcome.Succeeded, "{}", null) }, null, T0.AddSeconds(5));
        await runRepo.AddAsync(run1);

        var run2 = JobRun.Start(jobId, projectId, T0.AddMinutes(1), snap);
        run2.Complete(Array.Empty<ShardResult>(), null, T0.AddMinutes(1).AddSeconds(2));
        await runRepo.AddAsync(run2);

        var handler = new ListJobRunsHandler(runRepo);
        var result = await handler.HandleAsync(new ListJobRunsQuery(jobId));

        Assert.Equal(2, result.Count);
        Assert.True(result[0].StartedAt > result[1].StartedAt); // descending
    }

    [Fact]
    public async Task GetJobRun_returns_full_detail_with_artifacts()
    {
        var runRepo = new InMemoryJobRunRepository();
        var jobId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var snap = DefaultSnapshot();

        var run = JobRun.Start(jobId, projectId, T0, snap);
        run.Complete(
            new[] { new ShardResult(0, 0, WorkloadOutcome.Succeeded, @"{""key"":""val""}", "log text") },
            null, T0.AddSeconds(10));
        await runRepo.AddAsync(run);

        var handler = new GetJobRunHandler(runRepo);
        var result = await handler.HandleAsync(new GetJobRunQuery(run.Id));

        Assert.NotNull(result);
        Assert.Single(result!.ShardResults);
        Assert.Equal(@"{""key"":""val""}", result.ShardResults[0].Artifact);
        Assert.Equal("log text", result.ShardResults[0].Log);
        Assert.NotNull(result.Snapshot);
        Assert.Equal("image", result.Snapshot.MapSourceKind);
    }

    [Fact]
    public async Task GetJobRun_returns_null_for_unknown_run()
    {
        var handler = new GetJobRunHandler(new InMemoryJobRunRepository());
        var result = await handler.HandleAsync(new GetJobRunQuery(Guid.NewGuid()));
        Assert.Null(result);
    }

    // ── Multi-file code workloads ─────────────────────────────────────────────────────────────────

    private static CreateJobCommand MultiFileCmd(
        Guid projectId, string name, string runtimeId,
        IReadOnlyList<CodeFileDto> files, string entrypoint) => new(
        ProjectId: projectId,
        Name: name,
        Description: null,
        MapImage: null,
        MapRuntimeId: runtimeId,
        MapSource: null,
        MapEntrypoint: entrypoint,
        InputPayloads: new[] { "{}" },
        MapEnv: new Dictionary<string, string>(),
        ReduceImage: null,
        ReduceRuntimeId: null,
        ReduceSource: null,
        ReduceEntrypoint: null,
        ReduceEnv: null,
        ConcurrencyLimit: 1,
        SuccessExitCodes: new[] { 0 },
        PartialExitCodes: Array.Empty<int>(),
        AllowNetworkEgress: false,
        MapFiles: files);

    [Fact]
    public async Task CreateJob_multifile_persists_all_files_and_entrypoint()
    {
        var (createH, _) = BuildCreateHandler();
        var files = new[]
        {
            new CodeFileDto("index.js", "require('./lib/run')();"),
            new CodeFileDto("lib/run.js", "module.exports = () => {};"),
        };

        var view = await createH.HandleAsync(MultiFileCmd(Guid.NewGuid(), "multi", "node", files, "index.js"));

        Assert.Equal("code", view.MapSourceKind);
        Assert.Equal(2, view.MapFiles.Count);
        Assert.Equal("index.js", view.MapEntrypoint);
        Assert.Contains(view.MapFiles, f => f.Path == "lib/run.js");
        Assert.Equal("require('./lib/run')();", view.MapSource); // convenience: entry file content
    }

    [Fact]
    public async Task RunJob_multifile_sends_every_file_and_entrypoint_to_runner()
    {
        var (runHandler, jobRepo, _, runner) = BuildRunHandler();
        var createH = new CreateJobHandler(jobRepo, new RecordingUnitOfWork(), new FakeClock(T0));
        var files = new[]
        {
            new CodeFileDto("index.js", "a"),
            new CodeFileDto("lib/run.js", "b"),
        };

        var job = await createH.HandleAsync(MultiFileCmd(Guid.NewGuid(), "multi-run", "node", files, "index.js"));

        runner.EnqueueSuccessResults(1);
        await runHandler.HandleAsync(new RunJobCommand(job.Id));

        var req = Assert.Single(runner.ReceivedRequests);
        Assert.NotNull(req.CodeFiles);
        Assert.Equal(2, req.CodeFiles!.Count);
        Assert.Equal("index.js", req.Entrypoint);
        Assert.Contains(req.CodeFiles, f => f.Path == "lib/run.js" && f.Content == "b");
    }

    // ── UploadJobCode ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadJobCode_creates_new_code_job_when_absent()
    {
        var jobs = new InMemoryJobRepository();
        var handler = new UploadJobCodeHandler(jobs, new RecordingUnitOfWork(), new FakeClock(T0));
        var projectId = Guid.NewGuid();
        var files = new[] { new CodeFileDto("main.py", "print('hi')") };

        var view = await handler.HandleAsync(new UploadJobCodeCommand(
            JobId: null, ProjectId: projectId, JobName: "ingest", RuntimeId: "python",
            Entrypoint: "main.py", Files: files));

        Assert.Equal("ingest", view.Name);
        Assert.Equal("code", view.MapSourceKind);
        Assert.Equal("python", view.MapRuntimeId);
        Assert.Single(view.MapFiles);
        var stored = await jobs.ListForProjectAsync(projectId);
        Assert.Single(stored);
    }

    [Fact]
    public async Task UploadJobCode_replaces_files_preserving_inputs_and_concurrency()
    {
        var jobs = new InMemoryJobRepository();
        var createH = new CreateJobHandler(jobs, new RecordingUnitOfWork(), new FakeClock(T0));
        var clock = new FakeClock(T0);
        var uploadH = new UploadJobCodeHandler(jobs, new RecordingUnitOfWork(), clock);

        var created = await createH.HandleAsync(CodeCmd(
            Guid.NewGuid(), "ingest", "node", "old();", payloads: new[] { "{\"a\":1}", "{\"a\":2}" }, concurrency: 2));

        var view = await uploadH.HandleAsync(new UploadJobCodeCommand(
            JobId: created.Id, ProjectId: null, JobName: null, RuntimeId: "node",
            Entrypoint: "index.js", Files: new[] { new CodeFileDto("index.js", "fresh();") }));

        Assert.Equal("fresh();", view.MapSource);
        Assert.Equal("index.js", view.MapEntrypoint);
        Assert.Equal(2, view.ShardCount);          // payloads preserved
        Assert.Equal(2, view.ConcurrencyLimit);    // concurrency preserved
        Assert.Equal(new[] { "{\"a\":1}", "{\"a\":2}" }, view.InputPayloads);
    }
}
