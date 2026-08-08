using PlaceContext.Domain.Entities;

namespace PlaceContext.Jobs.Contracts.Integration;

/// <summary>
/// Integration boundary notified after a chain run and its final state have been persisted.
/// Implementations must not change the completed chain outcome when their follow-up work fails.
/// </summary>
public interface IChainRunCompletionObserver
{
    Task OnCompletedAsync(ChainRun run, CancellationToken cancellationToken = default);
}
