using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>
/// Cancels a running job run: kills all running shard/reduce containers, then marks the
/// run as Cancelled. Best-effort on the container side — if the containers already finished
/// the status is still set to Cancelled so callers get immediate feedback.
/// Idempotent — safe to call on a terminal run.
/// </summary>
public sealed class CancelJobRunHandler : ICommandHandler<CancelJobRunCommand, bool>
{
    private readonly IJobRunRepository _runs;
    private readonly IWorkloadRunner _runner;
    private readonly IJobsUnitOfWork _uow;
    private readonly IClock _clock;

    public CancelJobRunHandler(
        IJobRunRepository runs,
        IWorkloadRunner runner,
        IJobsUnitOfWork uow,
        IClock clock)
    {
        _runs = runs;
        _runner = runner;
        _uow = uow;
        _clock = clock;
    }

    public async Task<bool> HandleAsync(CancelJobRunCommand command, CancellationToken ct = default)
    {
        var run = await _runs.GetByIdAsync(command.RunId, ct);
        if (run is null) return false;
        if (run.Status != Domain.Entities.JobRunStatus.Running) return true; // idempotent

        // Cancel every shard + reduce container (best-effort).
        var snapshot = run.Snapshot;
        var shardCount = snapshot.InputPayloads.Count;
        var tasks = new List<Task>(shardCount + 1);

        for (var i = 0; i < shardCount; i++)
        {
            var correlationId = $"{run.Id:N}-map-{i}";
            tasks.Add(CancelWorkloadAsync(correlationId, ct));
        }

        if (snapshot.ReduceSource is not null)
        {
            var reduceCorrelationId = $"{run.Id:N}-reduce";
            tasks.Add(CancelWorkloadAsync(reduceCorrelationId, ct));
        }

        await Task.WhenAll(tasks);

        // Mark the run terminal.
        run.Cancel(_clock.UtcNow);
        await _runs.UpdateAsync(run, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }

    private async Task CancelWorkloadAsync(string correlationId, CancellationToken ct)
    {
        try { await _runner.CancelAsync(correlationId, ct); }
        catch { /* best-effort */ }
    }
}
