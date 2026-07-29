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

/// <summary>
/// Request body for PUT /api/v1/schedules/{id}. All fields are optional — only provided fields are
/// updated. Use this to rename, reschedule, change the event name, or enable/pause a trigger.
/// </summary>
public sealed record UpdateScheduleRequest(
    string? Name = null,
    string? CronExpression = null,
    string? EventName = null,
    bool? Enabled = null);

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

/// <summary>Translates between the management API's schedule DTOs and the internal read model.</summary>
internal static class ScheduleApiMapper
{
    public static ScheduleResponse ToResponse(TriggerView v) => new(
        v.Id, v.ProjectId, v.JobId ?? Guid.Empty, v.Name, v.Kind, v.Enabled,
        v.CronExpression, v.EventName, v.NextRunAt, v.LastFiredAt, v.CreatedAt);
}
