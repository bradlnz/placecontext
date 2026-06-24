using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Events;

/// <summary>An architectural decision was recorded.</summary>
public sealed record DecisionRecorded(ProjectId ProjectId, DecisionId DecisionId, DateTimeOffset OccurredAt)
    : IDomainEvent;
