using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>Permanently removes a trigger. Returns true if it existed.</summary>
public sealed record DeleteTriggerCommand(Guid TriggerId) : ICommand<bool>;
