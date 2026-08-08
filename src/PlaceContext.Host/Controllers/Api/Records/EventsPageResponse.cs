namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record EventsPageResponse(
    IReadOnlyList<EventTypePageResponse> Types,
    IReadOnlyList<EventOccurrencePageResponse> Log,
    IReadOnlyList<EventSubscriptionPageResponse> Triggers);
