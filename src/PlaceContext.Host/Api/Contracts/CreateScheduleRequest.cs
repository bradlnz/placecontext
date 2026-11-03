using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Api;

/// <summary>Request body for POST /api/v1/projects/{projectId}/schedules — creates a cron schedule or an
/// event subscription on a job. The project is inferred from the job.</summary>
public sealed record CreateScheduleRequest(
    Guid JobId,
    string Name,
    /// <summary>"Schedule" | "Event".</summary>
    string Kind,
    /// <summary>Required when Kind is "Schedule" — a standard 5-field cron expression.</summary>
    string? CronExpression,
    /// <summary>Required when Kind is "Event" — the event name this trigger subscribes to.</summary>
    string? EventName);
