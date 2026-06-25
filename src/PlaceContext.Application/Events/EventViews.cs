namespace PlaceContext.Application.Dtos;

/// <summary>Read model for an event type — a user-defined event or a reserved built-in.</summary>
public sealed record EventTypeView(
    string Name,
    string? Description,
    /// <summary>True for reserved system events that cannot be redefined or deleted.</summary>
    bool IsBuiltIn,
    string? PayloadSchema,
    DateTimeOffset? CreatedAt);

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
