using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Events;

/// <summary>A project's debt was recomputed.</summary>
public sealed record DebtRecomputed(ProjectId ProjectId, DebtScore Technical, DebtScore Agentic, DateTimeOffset OccurredAt)
    : IDomainEvent;
