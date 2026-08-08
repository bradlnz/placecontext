using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// High-level orchestrator for executing a job with its configured automatic retry policy.
/// Each retry attempt is a new <see cref="JobRun"/> row that replays the first attempt's
/// workload snapshot, preserving full history/telemetry and avoiding side-effect duplication
/// inside a single run.
/// </summary>
public interface IJobRunner
{
    /// <summary>
    /// Runs a job, applying <see cref="PlaceContext.Domain.Entities.Job.RetryCount"/> retries
    /// with <see cref="PlaceContext.Domain.Entities.Job.RetryDelaySeconds"/> between attempts
    /// when a run finishes with status <c>Failed</c>.
    /// </summary>
    /// <param name="jobId">The job to run.</param>
    /// <param name="inputPayload">Optional single-shard input override for the first attempt.</param>
    /// <param name="runId">Optional pre-allocated id for the first attempt.</param>
    /// <param name="replayOfRunId">Optional id of a prior run to replay (used by explicit replays; retries replay the first attempt of this call).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="JobRunDetailView"/> of the final attempt.</returns>
    Task<JobRunDetailView> RunAsync(
        Guid jobId,
        string? inputPayload = null,
        Guid? runId = null,
        Guid? replayOfRunId = null,
        CancellationToken ct = default);
}
