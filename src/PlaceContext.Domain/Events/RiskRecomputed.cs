using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Events;

/// <summary>A project's risk was recomputed.</summary>
public sealed record RiskRecomputed(ProjectId ProjectId, RiskScore Technical, RiskScore Process, DateTimeOffset OccurredAt)
    : IDomainEvent;
