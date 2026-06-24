using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Events;

/// <summary>A project's Markdown context document was appended to or replaced.</summary>
public sealed record ProjectContextUpdated(ProjectId ProjectId, DateTimeOffset OccurredAt)
    : IDomainEvent;
