namespace PlaceContext.Application.Dtos;

/// <summary>Read model for an event type — a user-defined event or a reserved built-in.</summary>
public sealed record EventTypeView(
    string Name,
    string? Description,
    /// <summary>True for reserved system events that cannot be redefined or deleted.</summary>
    bool IsBuiltIn,
    string? PayloadSchema,
    DateTimeOffset? CreatedAt);
