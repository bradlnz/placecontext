namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobRunDetailPageResponse(
    Guid Id,
    Guid JobId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int AttemptNumber,
    Guid? OriginalRunId,
    IReadOnlyList<JobRunShardPageResponse> Shards);
