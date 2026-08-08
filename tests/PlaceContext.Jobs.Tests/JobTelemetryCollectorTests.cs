using PlaceContext.Application.Observability;
using PlaceContext.Jobs.Infrastructure.Observability;
using Xunit;

namespace PlaceContext.Jobs.Tests;

/// <summary>
/// JobTelemetryCollector against the real <c>PlaceContext.Jobs</c> ActivitySource/Meter — the same
/// instruments RunJobHandler emits — rather than mocking the diagnostics API. Both the
/// ActivityListener's Stop callback and the MeterListener's measurement callback fire synchronously
/// (no background thread), so these assertions are deterministic; each test owns a fresh collector
/// (disposed via `using`) so its listener doesn't see another test's activities.
/// </summary>
[Collection(JobTelemetryCollection.Name)]
public class JobTelemetryCollectorTests
{
    [Fact]
    public void RecentRuns_CapturesARunActivityWithItsChildShard()
    {
        using var collector = new JobTelemetryCollector();

        var jobId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        using (var runSpan = JobTelemetry.Activity.StartActivity("job.run"))
        {
            Assert.NotNull(runSpan); // sanity: the collector's listener actually enabled recording
            runSpan!.SetTag("job.id", jobId);
            runSpan.SetTag("job.name", "nightly-encode");
            runSpan.SetTag("project.id", projectId);
            runSpan.SetTag("job.replay", false);
            runSpan.SetTag("run.id", runId);

            using (var shardSpan = JobTelemetry.Activity.StartActivity("job.shard"))
            {
                shardSpan!.SetTag("run.id", runId);
                shardSpan.SetTag("shard.index", 0);
                shardSpan.SetTag("shard.outcome", "Succeeded");
                shardSpan.SetTag("shard.exit_code", 0);
            }

            // Set after the map phase, exactly as RunJobHandler does before the run span disposes.
            runSpan.SetTag("run.status", "Succeeded");
            runSpan.SetTag("run.shards", 1);
        }

        var run = Assert.Single(collector.RecentRuns(10));
        Assert.Equal(runId, run.RunId);
        Assert.Equal(jobId, run.JobId);
        Assert.Equal("nightly-encode", run.JobName);
        Assert.Equal(projectId, run.ProjectId);
        Assert.Equal("Succeeded", run.Status);
        Assert.False(run.Replay);

        var shard = Assert.Single(run.Shards);
        Assert.Equal(0, shard.Index);
        Assert.Equal("Succeeded", shard.Outcome);
        Assert.Equal(0, shard.ExitCode);

        Assert.Single(collector.RunsForJob(jobId, 10));
        Assert.Empty(collector.RunsForJob(Guid.NewGuid(), 10));
    }

    [Fact]
    public void Snapshot_AggregatesCountersAndHistogramsAcrossMeasurements()
    {
        using var collector = new JobTelemetryCollector();

        JobTelemetry.RunsStarted.Add(1, new("job.id", "x"), new("project.id", "y"), new("replay", false));
        JobTelemetry.RunsCompleted.Add(1, new("status", "Succeeded"), new("job.id", "x"));
        JobTelemetry.RunsCompleted.Add(1, new("status", "Failed"), new("job.id", "x"));
        JobTelemetry.ShardsCompleted.Add(3, new KeyValuePair<string, object?>("outcome", "Succeeded"));
        JobTelemetry.RunDuration.Record(100, new("status", "Succeeded"), new("job.id", "x"));
        JobTelemetry.RunDuration.Record(300, new("status", "Failed"), new("job.id", "x"));

        var snapshot = collector.Snapshot();

        Assert.Equal(1, snapshot.RunsStarted);
        Assert.Equal(1, snapshot.RunsCompletedByStatus["Succeeded"]);
        Assert.Equal(1, snapshot.RunsCompletedByStatus["Failed"]);
        Assert.Equal(3, snapshot.ShardsCompletedByOutcome["Succeeded"]);
        Assert.NotNull(snapshot.RunDuration);
        Assert.Equal(2, snapshot.RunDuration!.Count);
        Assert.Equal(100, snapshot.RunDuration.MinMs);
        Assert.Equal(300, snapshot.RunDuration.MaxMs);
        Assert.Equal(200, snapshot.RunDuration.AvgMs);
    }

    [Fact]
    public void RecentRuns_IsBoundedAndNewestFirst()
    {
        using var collector = new JobTelemetryCollector();
        var runIds = new List<Guid>();

        for (var i = 0; i < 205; i++)
        {
            var runId = Guid.NewGuid();
            runIds.Add(runId);
            using var runSpan = JobTelemetry.Activity.StartActivity("job.run");
            runSpan!.SetTag("run.id", runId);
            runSpan.SetTag("job.id", Guid.NewGuid());
            runSpan.SetTag("run.status", "Succeeded");
        }

        var runs = collector.RecentRuns(1000);
        Assert.Equal(200, runs.Count); // ring buffer caps at 200, dropping the oldest 5
        Assert.Equal(runIds[^1], runs[0].RunId); // newest first
    }
}
