using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Events;

/// <summary>A project was registered (promoted from discovered).</summary>
public sealed record ProjectRegistered(ProjectId ProjectId, ProjectName Name, DateTimeOffset OccurredAt)
    : IDomainEvent;
