namespace PlaceContext.Application.Ports;

/// <summary>One map shard captured from the OTel <c>job.shard</c> activity.</summary>
public sealed record ShardTelemetry(int Index, string? Outcome, int? ExitCode, double? DurationMs);

/// <summary>
/// One job run captured from the OTel <c>job.run</c> activity, reduced to the fields the UI wants —
/// avoids re-reading raw <see cref="System.Diagnostics.Activity"/> tags on every render.
/// </summary>
public sealed record JobRunTelemetry(
    Guid RunId,
    Guid JobId,
    string? JobName,
    Guid? ProjectId,
    string? Status,
    bool Replay,
    DateTimeOffset StartedAt,
    double? DurationMs,
    IReadOnlyList<ShardTelemetry> Shards);

/// <summary>Count + min/max/avg summary of a histogram instrument since process start.</summary>
public sealed record DurationSummary(long Count, double MinMs, double MaxMs, double AvgMs);

/// <summary>
/// Aggregate jobs-pipeline metrics since this process started — the Cluster page's stat tiles.
/// Counters are keyed by their OTel tag value (e.g. run status, shard outcome).
/// </summary>
public sealed record JobTelemetrySnapshot(
    long RunsStarted,
    IReadOnlyDictionary<string, long> RunsCompletedByStatus,
    IReadOnlyDictionary<string, long> ShardsCompletedByOutcome,
    DurationSummary? RunDuration,
    DurationSummary? ShardDuration);

/// <summary>
/// In-process reader over the jobs pipeline's OpenTelemetry instruments (see
/// <c>PlaceContext.Application.Observability.JobTelemetry</c>) — gives the UI a live view of traces
/// and metrics with no external collector required. The Infrastructure-side collector feeds this by
/// listening on the same <see cref="System.Diagnostics.ActivitySource"/>/
/// <see cref="System.Diagnostics.Metrics.Meter"/>; this port keeps the Application/UI free of that
/// diagnostics-SDK plumbing.
/// </summary>
public interface IJobTelemetryReader
{
    JobTelemetrySnapshot Snapshot();

    /// <summary>The most recent job-run traces across the whole process, newest first.</summary>
    IReadOnlyList<JobRunTelemetry> RecentRuns(int take = 50);

    /// <summary>The most recent job-run traces for one job, newest first.</summary>
    IReadOnlyList<JobRunTelemetry> RunsForJob(Guid jobId, int take = 20);
}
