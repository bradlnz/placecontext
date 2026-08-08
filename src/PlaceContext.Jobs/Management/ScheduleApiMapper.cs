using PlaceContext.Application.Dtos;
using PlaceContext.Jobs.Contracts.Management;

namespace PlaceContext.Jobs.Management;

public static class ScheduleApiMapper
{
    public static ScheduleResponse ToResponse(TriggerView view) => new(
        view.Id, view.ProjectId, view.JobId ?? Guid.Empty, view.Name, view.Kind, view.Enabled,
        view.CronExpression, view.EventName, view.NextRunAt, view.LastFiredAt, view.CreatedAt);
}
