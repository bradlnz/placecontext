using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>Lifecycle of a project PlaceContext is tracking.</summary>
public enum ProjectStatus
{
    Discovered,
    Registered,
    Graphified,
    Archived
}
