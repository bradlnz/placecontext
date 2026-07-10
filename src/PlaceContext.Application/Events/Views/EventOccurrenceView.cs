namespace PlaceContext.Application.Dtos;

/// <summary>Read model for one emitted event in the log.</summary>
public sealed record EventOccurrenceView(
    Guid Id,
    string Name,
    /// <summary>"User" | "Domain".</summary>
    string Source,
    Guid? ProjectId,
    string? Payload,
    DateTimeOffset OccurredAt,
    /// <summary>How many job triggers fired in response to this occurrence.</summary>
    int TriggeredRuns);
