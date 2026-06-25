using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Entity: a single emitted event — one row in the workspace's event log. Occurrences drive
/// event-trigger fan-out and are shown in the Events section. They are produced both by users/agents
/// (<see cref="EventSource.User"/>) and by the system itself (<see cref="EventSource.Domain"/>).
/// </summary>
public sealed class EventOccurrence
{
    private EventOccurrence(
        Guid id, string name, EventSource source, Guid? projectId, string? payload, DateTimeOffset occurredAt)
    {
        Id = id;
        Name = name;
        Source = source;
        ProjectId = projectId;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; }

    /// <summary>The event name (a user-defined or built-in event).</summary>
    public string Name { get; }

    public EventSource Source { get; }

    /// <summary>The project this event concerns, when applicable; null for workspace-wide events.</summary>
    public Guid? ProjectId { get; }

    /// <summary>Optional opaque payload (typically JSON). Never interpreted by the domain.</summary>
    public string? Payload { get; }

    public DateTimeOffset OccurredAt { get; }

    /// <summary>Records a user-emitted event occurrence.</summary>
    public static EventOccurrence Emit(string name, Guid? projectId, string? payload, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Event name must not be empty.", nameof(name));
        return new EventOccurrence(Guid.NewGuid(), name.Trim(), EventSource.User, projectId, payload, now);
    }

    /// <summary>Records a system-raised domain event occurrence.</summary>
    public static EventOccurrence Raise(string name, Guid? projectId, string? payload, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Event name must not be empty.", nameof(name));
        return new EventOccurrence(Guid.NewGuid(), name.Trim(), EventSource.Domain, projectId, payload, now);
    }

    /// <summary>Rehydrates from persisted state. Infrastructure only.</summary>
    public static EventOccurrence Rehydrate(
        Guid id, string name, EventSource source, Guid? projectId, string? payload, DateTimeOffset occurredAt)
        => new(id, name, source, projectId, payload, occurredAt);
}
