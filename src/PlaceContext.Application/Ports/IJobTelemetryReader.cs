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
    IReadOnlyList<ShardTelemetry> Shards,
    string? TraceId = null,
    string? SpanId = null);

/// <summary>One span node in a captured job trace tree (for the Observability waterfall).</summary>
public sealed record TraceSpanNode(
    string Name,
    string? TraceId,
    string? SpanId,
    string? ParentSpanId,
    DateTimeOffset StartedAt,
    double DurationMs,
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyList<TraceSpanNode> Children);

/// <summary>Count + min/max/avg summary of a histogram instrument since process start.</summary>
public sealed record DurationSummary(long Count, double MinMs, double MaxMs, double AvgMs);

/// <summary>One step captured from a <c>job.chain</c> activity's step summary — the job that ran at
/// a stage/branch position, its run id (once dispatched) and outcome/timing. Mirrors <see
/// cref="PlaceContext.Application.Dtos.ChainStepRunView"/> but sourced purely from OTel, like the
/// rest of this reader.</summary>
public sealed record ChainRunStepTelemetry(
    int StageIndex,
    int BranchIndex,
    Guid JobId,
    string? JobName,
    Guid? RunId,
    string? Status,
    double? DurationMs);

/// <summary>
/// One chain run captured from the OTel <c>job.chain</c> activity, reduced to the fields the UI
/// wants. <see cref="Steps"/> is the chain's own step summary (stage/branch position, run id,
/// outcome) attached to the activity as a tag when the chain finishes — see
/// <c>RunJobChainHandler</c> and the Infrastructure collector for how it's captured.
/// </summary>
public sealed record ChainRunTelemetry(
    Guid ChainRunId,
    Guid ChainId,
    string? ChainName,
    Guid? ProjectId,
    string? Status,
    DateTimeOffset StartedAt,
    double? DurationMs,
    IReadOnlyList<ChainRunStepTelemetry> Steps);

/// <summary>
/// Aggregate jobs-pipeline metrics since this process started — the Cluster page's stat tiles.
/// Counters are keyed by their OTel tag value (e.g. run status, shard outcome).
/// </summary>
public sealed record JobTelemetrySnapshot(
    long RunsStarted,
    IReadOnlyDictionary<string, long> RunsCompletedByStatus,
    IReadOnlyDictionary<string, long> ShardsCompletedByOutcome,
    DurationSummary? RunDuration,
    DurationSummary? ShardDuration,
    long ChainsStarted,
    IReadOnlyDictionary<string, long> ChainsCompletedByStatus,
    DurationSummary? ChainDuration);

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

    /// <summary>The most recent chain-run traces across the whole process, newest first.</summary>
    IReadOnlyList<ChainRunTelemetry> RecentChainRuns(int take = 50);

    /// <summary>
    /// Full in-process span tree for a job run (run → shards), when this process executed it.
    /// Empty when the run was on another replica or before this process started.
    /// </summary>
    IReadOnlyList<TraceSpanNode> TraceForRun(Guid runId);
}
