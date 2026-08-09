namespace PlaceContext.Jobs.Contracts.Api;

public sealed record EventsPageResponse(
    IReadOnlyList<EventTypePageResponse> Types,
    IReadOnlyList<EventOccurrencePageResponse> Log,
    IReadOnlyList<EventSubscriptionPageResponse> Triggers);
