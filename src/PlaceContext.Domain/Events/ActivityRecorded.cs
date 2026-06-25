using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Events;

/// <summary>A change was appended to a project's ledger.</summary>
public sealed record ActivityRecorded(ProjectId ProjectId, ActivityRecordId ChangeId, Author Author, DateTimeOffset OccurredAt)
    : IDomainEvent;
