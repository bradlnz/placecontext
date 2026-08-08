using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record DeleteCrmCalendarCommand(Guid CalendarId) : ICommand<bool>, IRequiresPermission
{ public string RequiredPermission => Permission.DataWrite; }
