using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Defines (or updates) a user event type for this workspace. The name must not collide with a
/// reserved built-in event.</summary>
public sealed record DefineEventTypeCommand(
    string Name,
    string? Description,
    string? PayloadSchema) : ICommand<EventTypeView>;
