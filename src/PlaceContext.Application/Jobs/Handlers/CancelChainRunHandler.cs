using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Shared;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Persists workflow cancellation before stopping its child jobs, so the driver cannot
/// advance while cleanup runs. Pending steps become Cancelled instead of Skipped.
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
        if (chainRun is null)
        {
            return false;
        }
        if (chainRun.Status is not (ChainRunStatus.Running or ChainRunStatus.Waiting or ChainRunStatus.AwaitingInput))
        {
            return true;
        }
        var jobRuns = new List<Guid>();
        var now = _clock.UtcNow;
        for (var index = 0; index < chainRun.Steps.Count; index++)
        {
            var step = chainRun.Steps[index];
            if (step.Status != ChainStepStatus.Running)
            {
                continue;
            }
            if (step.RunId is { } runId)
            {
                jobRuns.Add(runId);
            }
            chainRun.MarkStepFinished(step.Index, step.RunId, ChainStepStatus.Cancelled, now);
        }
        chainRun.Complete(ChainRunStatus.Cancelled, chainRun.FinalOutput, now);
        await _runs.UpdateAsync(chainRun, ct);
        await _uow.SaveChangesAsync(ct);
        await Task.WhenAll(jobRuns.Select(runId => SafeCancelRunAsync(runId, ct)));
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
