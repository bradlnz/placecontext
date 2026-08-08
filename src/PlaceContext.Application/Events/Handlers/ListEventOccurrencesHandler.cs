using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListEventOccurrencesHandler
    : IQueryHandler<ListEventOccurrencesQuery, IReadOnlyList<EventOccurrenceView>>
{
    private readonly IEventRepository _events;

    public ListEventOccurrencesHandler(IEventRepository events) => _events = events;

    public async Task<IReadOnlyList<EventOccurrenceView>> HandleAsync(
        ListEventOccurrencesQuery query, CancellationToken ct = default)
    {
        var occurrences = await _events.ListOccurrencesAsync(query.Take, ct);
        // TriggeredRuns is only known at emit time; the log view reports 0 (not persisted per-occurrence).
        return occurrences
            .Select(o => new EventOccurrenceView(
                o.Id, o.Name, o.Source.ToString(), o.ProjectId, o.Payload, o.OccurredAt, 0))
            .ToList();
    }
}
