using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Compact status projection of one job run — no shard/artifact payloads.</summary>
public sealed record JobRunStatusRow(
    Guid RunId,
    Guid JobId,
    string JobName,
    Guid ProjectId,
    JobRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);
