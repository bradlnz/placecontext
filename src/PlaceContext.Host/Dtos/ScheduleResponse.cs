using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Api;

/// <summary>Public read model for a schedule (cron) or event trigger.</summary>
public sealed record ScheduleResponse(
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
