using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListCrmCalendarsHandler : IQueryHandler<ListCrmCalendarsQuery, IReadOnlyList<CrmCalendarView>>
{
    private readonly ICrmCalendarRepository _calendars; public ListCrmCalendarsHandler(ICrmCalendarRepository calendars) => _calendars = calendars;
    public async Task<IReadOnlyList<CrmCalendarView>> HandleAsync(ListCrmCalendarsQuery query, CancellationToken ct = default)
        => (await _calendars.ListForProjectAsync(query.ProjectId, ct)).Select(SaveCrmCalendarHandler.Map).ToArray();
}
