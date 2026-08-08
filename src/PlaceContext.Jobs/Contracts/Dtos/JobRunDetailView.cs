namespace PlaceContext.Application.Dtos;

/// <summary>Full job run detail including all shard and reduce artifacts.</summary>
public sealed record JobRunDetailView(
    Guid Id,
    Guid JobId,
    Guid ProjectId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<ShardResultView> ShardResults,
    ReduceResultView? ReduceResult,
    JobRunSnapshotView Snapshot,
    /// <summary>1-based attempt number for this run. Retries increment this value.</summary>
    int AttemptNumber = 1,
    /// <summary>Id of the first run in this retry chain. Null for the first attempt.</summary>
    Guid? OriginalRunId = null);
