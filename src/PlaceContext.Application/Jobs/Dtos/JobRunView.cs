namespace PlaceContext.Application.Dtos;

/// <summary>Read model for a job run summary (list view).</summary>
public sealed record JobRunView(
    Guid Id,
    Guid JobId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int ShardCount,
    int SucceededShards,
    int PartialShards,
    int FailedShards);
