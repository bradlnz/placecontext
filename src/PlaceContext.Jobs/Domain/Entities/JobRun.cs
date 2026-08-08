using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: one execution of a <see cref="Job"/>. Immutable after terminal status is reached.
/// Status transitions: Queued → Running → (Succeeded | Partial | Failed).
/// Status aggregation is a pure domain function driven by the job's <see cref="ExitCodePolicy"/> —
/// no domain-specific exit codes are hardcoded here.
///
/// A <see cref="WorkloadSnapshot"/> is captured at run-start so the exact spec that was executed is
/// preserved regardless of later edits to the parent Job.
/// </summary>
public sealed class JobRun : AggregateRoot
{
    private readonly List<ShardResult> _shardResults = new();

    private JobRun(
        Guid id,
        Guid jobId,
        Guid projectId,
        JobRunStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset? finishedAt,
        ReduceResult? reduceResult,
        WorkloadSnapshot snapshot,
        int attemptNumber,
        Guid? originalRunId)
    {
        Id = id;
        JobId = jobId;
        ProjectId = projectId;
        Status = status;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
        ReduceResult = reduceResult;
        Snapshot = snapshot;
        AttemptNumber = attemptNumber;
        OriginalRunId = originalRunId;
    }

    public Guid Id { get; }
    public Guid JobId { get; }
    public Guid ProjectId { get; }
    public JobRunStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public IReadOnlyList<ShardResult> ShardResults => _shardResults;
    public ReduceResult? ReduceResult { get; private set; }

    /// <summary>
    /// Snapshot of the workload spec as it was at run-start.
    /// Guarantees run-history reproducibility even when the parent Job is later edited.
    /// </summary>
    public WorkloadSnapshot Snapshot { get; }

    /// <summary>1-based attempt number for this run. Retries increment this value.</summary>
    public int AttemptNumber { get; private set; }

    /// <summary>Id of the first run in this retry chain. Null for the first attempt.</summary>
    public Guid? OriginalRunId { get; private set; }

    /// <summary>Factory: creates a new run in the Running state, snapshotting the current job spec.
    /// Callers may pre-allocate <paramref name="id"/> so the run is addressable (progress
    /// notifications, chain step links) before the handler returns.</summary>
    public static JobRun Start(Guid jobId, Guid projectId, DateTimeOffset startedAt, WorkloadSnapshot snapshot,
        Guid? id = null, int attemptNumber = 1, Guid? originalRunId = null)
    {
        if (jobId == Guid.Empty)
            throw new ArgumentException("JobId must not be empty.", nameof(jobId));
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        if (id == Guid.Empty)
            throw new ArgumentException("A pre-allocated run id must not be empty.", nameof(id));
        if (attemptNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number must be >= 1.");
        ArgumentNullException.ThrowIfNull(snapshot);

        return new JobRun(id ?? Guid.NewGuid(), jobId, projectId, JobRunStatus.Running, startedAt, null, null, snapshot,
            attemptNumber, originalRunId);
    }

    /// <summary>
    /// Aggregates all shard results and (optionally) a reduce result to compute the final run status.
    /// Pure domain logic — driven entirely by <paramref name="shardResults"/> outcomes and the reduce step's success flag.
    /// After this call the run is in a terminal state.
    /// </summary>
    public void Complete(
        IEnumerable<ShardResult> shardResults,
        ReduceResult? reduceResult,
        DateTimeOffset finishedAt)
    {
        if (Status != JobRunStatus.Running)
            throw new InvalidOperationException($"Cannot complete a run that is in status {Status}.");

        _shardResults.Clear();
        _shardResults.AddRange(shardResults);
        ReduceResult = reduceResult;
        FinishedAt = finishedAt;
        Status = DeriveStatus(_shardResults, reduceResult);
    }

    /// <summary>Rehydrates a run from persisted state. Infrastructure only.</summary>
    public static JobRun Rehydrate(
        Guid id,
        Guid jobId,
        Guid projectId,
        JobRunStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset? finishedAt,
        IEnumerable<ShardResult> shardResults,
        ReduceResult? reduceResult,
        WorkloadSnapshot snapshot,
        int attemptNumber = 1,
        Guid? originalRunId = null)
    {
        var run = new JobRun(id, jobId, projectId, status, startedAt, finishedAt, reduceResult, snapshot,
            attemptNumber, originalRunId);
        run._shardResults.AddRange(shardResults);
        return run;
    }

    /// <summary>
    /// Pure status-aggregation: if any shard failed → Failed; if any partial → Partial;
    /// if reduce step present and failed → Failed; otherwise → Succeeded.
    /// </summary>
    /// <summary>
    /// Cancels the run: sets the terminal status to Cancelled, discarding any partial results.
    /// Safe to call on a running run only — no-op if already terminal.
    /// </summary>
    public void Cancel(DateTimeOffset finishedAt)
    {
        if (Status != JobRunStatus.Running) return;
        FinishedAt = finishedAt;
        Status = JobRunStatus.Cancelled;
    }

    public static JobRunStatus DeriveStatus(
        IReadOnlyList<ShardResult> shardResults,
        ReduceResult? reduceResult)
    {
        // Any hard failure (shard or reduce) dominates.
        if (shardResults.Any(s => s.Outcome == WorkloadOutcome.Failed))
            return JobRunStatus.Failed;

        if (reduceResult is { Succeeded: false })
            return JobRunStatus.Failed;

        // If all shards succeeded but some are partial, the run is partial.
        if (shardResults.Any(s => s.Outcome == WorkloadOutcome.Partial))
            return JobRunStatus.Partial;

        return JobRunStatus.Succeeded;
    }
}
