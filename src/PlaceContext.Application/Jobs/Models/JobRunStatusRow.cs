using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Jobs;

/// <summary>Compact status projection of one job run — no shard/artifact payloads.</summary>
public sealed record JobRunStatusRow(
    Guid RunId,
    Guid JobId,
    string JobName,
    Guid ProjectId,
    JobRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);
