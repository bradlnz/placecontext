using PlaceContext.Domain.Entities;
using PlaceContext.Jobs.Contracts.Integration;
using PlaceContext.Jobs.Integration;

namespace PlaceContext.Jobs.Infrastructure.Integration;

public sealed class JobCrmCompletionObserver(IJobCrmClient crm) : IChainRunCompletionObserver
{
    public Task OnCompletedAsync(ChainRun run, CancellationToken cancellationToken = default)
    {
        if (run.CrmClientId is not { } clientId) return Task.CompletedTask;

        return crm.NotifyChainCompletedAsync(
            new JobCrmChainCompletion(
                run.ProjectId,
                clientId,
                run.ChainId,
                run.Id,
                run.ChainName,
                run.Status.ToString(),
                run.StartedAt,
                run.FinishedAt,
                run.Steps.Select(step => step.RunId).OfType<Guid>().Distinct().ToArray()),
            cancellationToken);
    }
}
