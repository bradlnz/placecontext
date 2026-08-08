using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record SaveCrmCalendarCommand(Guid ProjectId, string Name, string Color, Guid? CalendarId = null)
    : ICommand<CrmCalendarView>, IRequiresPermission
{ public string RequiredPermission => Permission.DataWrite; }
