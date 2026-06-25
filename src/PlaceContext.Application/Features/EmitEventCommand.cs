using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Emits an event occurrence, fanning out to every enabled event-trigger subscribed to the name (each
/// enqueues a job run). The name may be a user-defined or a built-in event. Payload is opaque.
/// </summary>
public sealed record EmitEventCommand(
    string Name,
    Guid? ProjectId,
    string? Payload) : ICommand<EventOccurrenceView>;
