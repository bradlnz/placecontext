using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;
public sealed record ListCrmCalendarsQuery(Guid ProjectId) : IQuery<IReadOnlyList<CrmCalendarView>>;
