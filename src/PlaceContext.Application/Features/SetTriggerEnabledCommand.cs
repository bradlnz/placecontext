using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Enables or pauses a trigger. Re-enabling a schedule recomputes its next-run time.</summary>
public sealed record SetTriggerEnabledCommand(Guid TriggerId, bool Enabled) : ICommand<TriggerView>;
