namespace PlaceContext.Application.Dtos;

/// <summary>Read model for a <see cref="PlaceContext.Domain.Entities.JobTrigger"/>.</summary>
public sealed record TriggerView(
    Guid Id,
    Guid ProjectId,
    Guid JobId,
    string Name,
    /// <summary>"Schedule" | "Event".</summary>
    string Kind,
    bool Enabled,
    string? CronExpression,
    string? EventName,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastFiredAt,
    DateTimeOffset CreatedAt);
