namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Origin of an <see cref="PlaceContext.Domain.Entities.EventOccurrence"/>: emitted explicitly by a
/// <see cref="User"/> (via the portal/MCP) or raised by the system itself as a <see cref="Domain"/>
/// event (e.g. activity recorded, a job run completed).
/// </summary>
public enum EventSource
{
    /// <summary>Emitted explicitly by a user or agent.</summary>
    User,

    /// <summary>Raised by the system in reaction to a domain happening.</summary>
    Domain
}
