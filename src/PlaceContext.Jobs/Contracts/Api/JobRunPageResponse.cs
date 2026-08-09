namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobRunPageResponse(
    Guid Id,
    Guid JobId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string StartedAtDisplay,
    string? DurationDisplay,
    int ShardCount,
    int SucceededShards,
    int PartialShards,
    int FailedShards);
