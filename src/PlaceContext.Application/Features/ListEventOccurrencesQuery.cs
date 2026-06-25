using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Lists the most recent emitted events (the event log).</summary>
public sealed record ListEventOccurrencesQuery(int Take = 50) : IQuery<IReadOnlyList<EventOccurrenceView>>;
