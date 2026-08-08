namespace PlaceContext.Jobs.Contracts.Management;

/// <summary>Creates a cron schedule or event subscription on a job.</summary>
public sealed record CreateScheduleRequest(
    Guid JobId,
    string Name,
    string Kind,
    string? CronExpression,
    string? EventName);
