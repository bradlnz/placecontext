namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record JobRunDetailPageResponse(
    Guid Id,
    Guid JobId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int AttemptNumber,
    Guid? OriginalRunId,
    IReadOnlyList<JobRunShardPageResponse> Shards);
