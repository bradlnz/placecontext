using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.TestSupport;

/// <summary>In-memory chain-run store. Records every persisted snapshot of each run's step
/// statuses, so tests can assert the pipeline progression (pending → running → outcome) that the
/// portal observes while a chain executes.</summary>
public sealed class InMemoryChainRunRepository : IChainRunRepository
{
    private readonly Dictionary<Guid, ChainRun> _runs = new();

    /// <summary>Step-status snapshots in the order they were saved, e.g. "Running,Pending".</summary>
    public List<string> SavedStepSnapshots { get; } = new();

    public Task AddAsync(ChainRun run, CancellationToken ct = default)
    {
        _runs[run.Id] = run;
        SavedStepSnapshots.Add(Snapshot(run));
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ChainRun run, CancellationToken ct = default)
    {
        _runs[run.Id] = run;
        SavedStepSnapshots.Add(Snapshot(run));
        return Task.CompletedTask;
    }

    public Task<ChainRun?> GetByIdAsync(Guid chainRunId, CancellationToken ct = default)
        => Task.FromResult(_runs.TryGetValue(chainRunId, out var r) ? r : null);

    public Task<IReadOnlyList<ChainRun>> ListForChainAsync(Guid chainId, int take, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ChainRun>>(_runs.Values
            .Where(r => r.ChainId == chainId)
            .OrderByDescending(r => r.StartedAt)
            .Take(take)
            .ToList());

    private static string Snapshot(ChainRun run)
        => string.Join(",", run.Steps.Select(s => s.Status.ToString()));
}
