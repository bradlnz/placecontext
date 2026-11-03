using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Api;

/// <summary>
/// Request body for PUT /api/v1/schedules/{id}. All fields are optional — only provided fields are
/// updated. Use this to rename, reschedule, change the event name, or enable/pause a trigger.
/// </summary>
public sealed record UpdateScheduleRequest(
    string? Name = null,
    string? CronExpression = null,
    string? EventName = null,
    bool? Enabled = null);
