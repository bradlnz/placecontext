namespace PlaceContext.Application.DataSources;

public sealed record SourceExecution(Guid Id, string Status, string Stage, DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt = null, int Processed = 0, int Total = 0, int Held = 0);
