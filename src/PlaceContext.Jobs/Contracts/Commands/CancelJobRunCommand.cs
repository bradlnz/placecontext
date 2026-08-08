using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Cancels a running job run: kills the workload containers/pods and marks the run as Cancelled.
/// Idempotent — safe to call on a run that already reached a terminal state.
/// </summary>
public sealed record CancelJobRunCommand(Guid RunId) : ICommand<bool>;
