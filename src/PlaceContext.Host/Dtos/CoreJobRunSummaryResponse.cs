namespace PlaceContext.Host.Api;

public sealed record CoreJobRunSummaryResponse(
    Guid Id,
    Guid JobId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int ShardCount,
    int SucceededShards,
    int PartialShards,
    int FailedShards);
