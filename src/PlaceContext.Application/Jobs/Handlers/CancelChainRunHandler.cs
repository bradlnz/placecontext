using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Cancels a running chain run: cancels every still-running step job run, then marks
/// the chain run as Cancelled. Pending steps become Cancelled instead of Skipped.
/// Idempotent — safe to call on a terminal chain run.
/// </summary>
public sealed class CancelChainRunHandler : ICommandHandler<CancelChainRunCommand, bool>
{
    private readonly IChainRunRepository _runs;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly IDispatcher _dispatcher;

    public CancelChainRunHandler(
        IChainRunRepository runs,
        IUnitOfWork uow,
        IClock clock,
        IDispatcher dispatcher)
    {
        _runs = runs;
        _uow = uow;
        _clock = clock;
        _dispatcher = dispatcher;
    }

    public async Task<bool> HandleAsync(CancelChainRunCommand command, CancellationToken ct = default)
    {
        var chainRun = await _runs.GetByIdAsync(command.ChainRunId, ct);
        if (chainRun is null) return false;
        if (chainRun.Status != ChainRunStatus.Running) return true;

        // Cancel every step that has a running job run, then mark each as Cancelled.
        var cancelTasks = new List<(int Index, Task Task)>();
        foreach (var step in chainRun.Steps)
        {
            if (step.Status is ChainStepStatus.Running && step.RunId is { } runId)
            {
                var idx = step.Index;
                cancelTasks.Add((idx, SafeCancelRunAsync(runId, ct)));
            }
        }
        await Task.WhenAll(cancelTasks.Select(t => t.Task));
        var now = _clock.UtcNow;
        foreach (var (idx, _) in cancelTasks)
            chainRun.MarkStepFinished(idx, chainRun.Steps[idx].RunId, ChainStepStatus.Cancelled, now);

        // Mark the entire chain run as Cancelled.
        chainRun.Complete(ChainRunStatus.Cancelled, chainRun.FinalOutput, now);
        await _runs.UpdateAsync(chainRun, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }

    private async Task SafeCancelRunAsync(Guid runId, CancellationToken ct)
    {
        try
        {
            await _dispatcher.Send(new CancelJobRunCommand(runId), ct);
        }
        catch
        {
            // best-effort — the container may have already exited
        }
    }
}
