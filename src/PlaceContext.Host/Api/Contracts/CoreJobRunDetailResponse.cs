namespace PlaceContext.Host.Api;

public sealed record CoreJobRunDetailResponse(
    Guid Id,
    Guid JobId,
    Guid ProjectId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int AttemptNumber,
    Guid? OriginalRunId,
    CoreRunSnapshotResponse Snapshot,
    IReadOnlyList<CoreShardResult> Shards,
    CoreReduceResult? ReduceResult);
