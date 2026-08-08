using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record CreateCrmAppointmentCommand(Guid ProjectId, Guid? CalendarId, Guid? ClientId, string Title,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Location, string? Notes, Guid? AppointmentId = null)
    : ICommand<CrmAppointmentView>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
