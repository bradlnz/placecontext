using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Events;

/// <summary>A project's knowledge graph was (re)built.</summary>
public sealed record GraphRebuilt(ProjectId ProjectId, GraphSnapshotRef Snapshot, DateTimeOffset OccurredAt)
    : IDomainEvent;
