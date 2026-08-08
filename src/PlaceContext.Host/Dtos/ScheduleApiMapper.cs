using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Api;

/// <summary>Translates between the management API's schedule DTOs and the internal read model.</summary>
internal static class ScheduleApiMapper
{
    public static ScheduleResponse ToResponse(TriggerView v) => new(
        v.Id, v.ProjectId, v.JobId ?? Guid.Empty, v.Name, v.Kind, v.Enabled,
        v.CronExpression, v.EventName, v.NextRunAt, v.LastFiredAt, v.CreatedAt);
}
