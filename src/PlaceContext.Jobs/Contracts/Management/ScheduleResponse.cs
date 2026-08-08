namespace PlaceContext.Jobs.Contracts.Management;

/// <summary>Public read model for a cron schedule or event trigger.</summary>
public sealed record ScheduleResponse(
    Guid Id,
    Guid ProjectId,
    Guid JobId,
    string Name,
    string Kind,
    bool Enabled,
    string? CronExpression,
    string? EventName,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastFiredAt,
    DateTimeOffset CreatedAt);
