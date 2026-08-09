namespace PlaceContext.Jobs.Contracts.Api;

public sealed record EventOccurrencePageResponse(
    Guid Id,
    string Name,
    string Source,
    string SourceLabel,
    string? Payload,
    DateTimeOffset OccurredAt,
    string OccurredAtDisplay,
    int TriggeredRuns);
