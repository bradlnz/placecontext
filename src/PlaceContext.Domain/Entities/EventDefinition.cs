using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: a user-defined event type for this workspace. Defining an event lets triggers
/// subscribe to it by <see cref="Name"/> and lets users/agents emit occurrences of it. Built-in
/// system events (see <see cref="BuiltInEvents"/>) are not stored as definitions — their names are
/// reserved and cannot be redefined.
/// </summary>
public sealed class EventDefinition : AggregateRoot
{
    private EventDefinition(
        Guid id, string name, string? description, string? payloadSchema,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Id = id;
        Name = name;
        Description = description;
        PayloadSchema = payloadSchema;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    /// <summary>Unique event name within the workspace (e.g. "deploy.finished").</summary>
    public string Name { get; private set; }

    /// <summary>Optional human description of when this event is emitted.</summary>
    public string? Description { get; private set; }

    /// <summary>Optional freetext/JSON describing the expected payload shape. Not enforced.</summary>
    public string? PayloadSchema { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates a user-defined event type. The name must not collide with a built-in event.</summary>
    public static EventDefinition Create(string name, string? description, string? payloadSchema, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Event name must not be empty.", nameof(name));
        if (BuiltInEvents.IsBuiltIn(name.Trim()))
            throw new ArgumentException($"'{name.Trim()}' is a reserved built-in event name.", nameof(name));

        return new EventDefinition(
            Guid.NewGuid(), name.Trim(), description?.Trim(), payloadSchema?.Trim(), now, now);
    }

    /// <summary>Rehydrates from persisted state. Infrastructure only.</summary>
    public static EventDefinition Rehydrate(
        Guid id, string name, string? description, string? payloadSchema,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(id, name, description, payloadSchema, createdAt, updatedAt);

    /// <summary>Updates the description and payload schema.</summary>
    public void Update(string? description, string? payloadSchema, DateTimeOffset now)
    {
        Description = description?.Trim();
        PayloadSchema = payloadSchema?.Trim();
        UpdatedAt = now;
    }
}
