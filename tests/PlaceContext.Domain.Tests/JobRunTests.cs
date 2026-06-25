using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Domain.Tests;

/// <summary>
/// Domain tests for JobRun status aggregation, transitions, and the ExitCodePolicy classification.
/// All tests are purely in-process — no I/O, no infrastructure.
/// </summary>
public class JobRunTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static WorkloadSnapshot DefaultSnapshot()
    {
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        return WorkloadSnapshot.From(mapSpec, null, 1);
    }

    // ---- ExitCodePolicy classification ----

    [Fact]
    public void ExitCodePolicy_default_maps_zero_to_succeeded()
    {
        var outcome = ExitCodePolicy.Default.Classify(0);
        Assert.Equal(WorkloadOutcome.Succeeded, outcome);
    }

    [Fact]
    public void ExitCodePolicy_default_maps_nonzero_to_failed()
    {
        var outcome = ExitCodePolicy.Default.Classify(1);
        Assert.Equal(WorkloadOutcome.Failed, outcome);
    }

    [Theory]
    [InlineData(0, WorkloadOutcome.Succeeded)]
    [InlineData(3, WorkloadOutcome.Partial)]
    [InlineData(1, WorkloadOutcome.Failed)]
    [InlineData(99, WorkloadOutcome.Failed)]
    public void ExitCodePolicy_custom_classifies_correctly(int exitCode, WorkloadOutcome expected)
    {
        var policy = new ExitCodePolicy(successCodes: new[] { 0 }, partialCodes: new[] { 3 });
        Assert.Equal(expected, policy.Classify(exitCode));
    }

    // ---- JobRun.Start ----

    [Fact]
    public void Start_creates_run_in_Running_status()
    {
        var run = JobRun.Start(JobId, ProjectId, T0, DefaultSnapshot());

        Assert.Equal(JobRunStatus.Running, run.Status);
        Assert.Equal(JobId, run.JobId);
        Assert.Equal(ProjectId, run.ProjectId);
        Assert.Equal(T0, run.StartedAt);
        Assert.Null(run.FinishedAt);
        Assert.Empty(run.ShardResults);
        Assert.Null(run.ReduceResult);
    }

    [Fact]
    public void Start_rejects_empty_job_id()
    {
        Assert.Throws<ArgumentException>(() => JobRun.Start(Guid.Empty, ProjectId, T0, DefaultSnapshot()));
    }

    [Fact]
    public void Start_rejects_empty_project_id()
    {
        Assert.Throws<ArgumentException>(() => JobRun.Start(JobId, Guid.Empty, T0, DefaultSnapshot()));
    }

    // ---- JobRun.Complete — status aggregation ----

    [Fact]
    public void Complete_all_succeeded_shards_yields_Succeeded()
    {
        var run = JobRun.Start(JobId, ProjectId, T0, DefaultSnapshot());
        var shards = new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "{}", null),
            new ShardResult(1, 0, WorkloadOutcome.Succeeded, "{}", null),
        };

        run.Complete(shards, null, T0.AddSeconds(10));

        Assert.Equal(JobRunStatus.Succeeded, run.Status);
        Assert.Equal(T0.AddSeconds(10), run.FinishedAt);
        Assert.Equal(2, run.ShardResults.Count);
    }

    [Fact]
    public void Complete_any_failed_shard_yields_Failed()
    {
        var run = JobRun.Start(JobId, ProjectId, T0, DefaultSnapshot());
        var shards = new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "{}", null),
            new ShardResult(1, 1, WorkloadOutcome.Failed, null, "error"),
        };

        run.Complete(shards, null, T0.AddSeconds(5));

        Assert.Equal(JobRunStatus.Failed, run.Status);
    }

    [Fact]
    public void Complete_partial_shard_with_no_failed_yields_Partial()
    {
        var run = JobRun.Start(JobId, ProjectId, T0, DefaultSnapshot());
        var shards = new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "{}", null),
            new ShardResult(1, 3, WorkloadOutcome.Partial, "{\"partial\":true}", null),
        };

        run.Complete(shards, null, T0.AddSeconds(5));

        Assert.Equal(JobRunStatus.Partial, run.Status);
    }

    [Fact]
    public void Complete_failed_reduce_step_yields_Failed_even_when_all_shards_succeeded()
    {
        var run = JobRun.Start(JobId, ProjectId, T0, DefaultSnapshot());
        var shards = new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "{}", null),
        };
        var reduce = new ReduceResult(exitCode: 1, succeeded: false, artifact: null, log: "reduce error");

        run.Complete(shards, reduce, T0.AddSeconds(15));

        Assert.Equal(JobRunStatus.Failed, run.Status);
    }

    [Fact]
    public void Complete_successful_reduce_step_with_all_succeeded_shards_yields_Succeeded()
    {
        var run = JobRun.Start(JobId, ProjectId, T0, DefaultSnapshot());
        var shards = new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, @"{""data"":1}", null),
        };
        var reduce = new ReduceResult(exitCode: 0, succeeded: true, artifact: @"{""summary"":1}", log: null);

        run.Complete(shards, reduce, T0.AddSeconds(20));

        Assert.Equal(JobRunStatus.Succeeded, run.Status);
        Assert.NotNull(run.ReduceResult);
        Assert.Equal(@"{""summary"":1}", run.ReduceResult!.Artifact);
    }

    [Fact]
    public void Complete_on_non_running_run_throws()
    {
        var run = JobRun.Start(JobId, ProjectId, T0, DefaultSnapshot());
        run.Complete(Array.Empty<ShardResult>(), null, T0.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() =>
            run.Complete(Array.Empty<ShardResult>(), null, T0.AddSeconds(2)));
    }

    // ---- DeriveStatus (static, pure) ----

    [Fact]
    public void DeriveStatus_empty_shard_list_with_no_reduce_yields_Succeeded()
    {
        // Edge case: a job that somehow runs 0 shards (blocked by Job.Create invariant, but tested independently).
        var status = JobRun.DeriveStatus(Array.Empty<ShardResult>(), null);
        Assert.Equal(JobRunStatus.Succeeded, status);
    }

    [Fact]
    public void DeriveStatus_failed_shard_dominates_over_partial()
    {
        var shards = new[]
        {
            new ShardResult(0, 1, WorkloadOutcome.Failed, null, null),
            new ShardResult(1, 3, WorkloadOutcome.Partial, "{}", null),
        };
        Assert.Equal(JobRunStatus.Failed, JobRun.DeriveStatus(shards, null));
    }

    // ---- Job.Create invariants ----

    [Fact]
    public void Job_Create_rejects_zero_input_payloads()
    {
        var map = new MapSpec("img", Array.Empty<string>(), new Dictionary<string, string>());
        Assert.Throws<ArgumentException>(() =>
            Job.Create(Guid.NewGuid(), "test", null, map, null, 1, ExitCodePolicy.Default, T0));
    }

    [Fact]
    public void Job_Create_rejects_concurrency_below_one()
    {
        var map = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Job.Create(Guid.NewGuid(), "test", null, map, null, 0, ExitCodePolicy.Default, T0));
    }

    [Fact]
    public void Job_Create_rejects_empty_name()
    {
        var map = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        Assert.Throws<ArgumentException>(() =>
            Job.Create(Guid.NewGuid(), "   ", null, map, null, 1, ExitCodePolicy.Default, T0));
    }

    [Fact]
    public void Job_Create_valid_with_reduce_spec()
    {
        var map = new MapSpec("img/map:latest", new[] { "{\"a\":1}", "{\"a\":2}" }, new Dictionary<string, string> { ["KEY"] = "val" });
        var reduce = new ReduceSpec("img/reduce:latest", new Dictionary<string, string>());

        var job = Job.Create(Guid.NewGuid(), "My Job", "does stuff", map, reduce, 2, ExitCodePolicy.Default, T0);

        Assert.True(job.HasReduceStep);
        Assert.Equal(2, job.MapSpec.ShardCount);
        Assert.Equal(2, job.ConcurrencyLimit);
    }
}
