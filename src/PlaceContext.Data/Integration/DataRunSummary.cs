namespace PlaceContext.Data.Integration;

public sealed record DataRunSummary(
    Guid Id,
    Guid JobId,
    string Status,
    DateTimeOffset StartedAt);
