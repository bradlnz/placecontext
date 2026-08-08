namespace PlaceContext.Application.Ports;

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
