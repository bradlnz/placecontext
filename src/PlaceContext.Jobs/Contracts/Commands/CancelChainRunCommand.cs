using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>
/// Cancels a running chain run: cancels every running step job run and marks the chain run as Cancelled.
/// Idempotent — safe to call on a chain run that already reached a terminal state.
/// </summary>
public sealed record CancelChainRunCommand(Guid ChainRunId) : ICommand<bool>;
