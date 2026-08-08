using PlaceContext.Application.Ports;
using PlaceContext.Crm.Domain.Persistence;
using PlaceContext.Jobs.Contracts.Integration;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Crm.Services;

/// <summary>
/// Tags every stored output from a terminal CRM chain run to its customer. Keeping this at the
/// chain boundary covers manual CRM runs, ingestion automations, and durable wait continuations.
/// </summary>
public sealed class CrmArtifactAssociationService : IChainRunCompletionObserver
{
    private readonly IRunArtifactLinkRepository _runArtifacts;
    private readonly ICrmClientArtifactRepository _clientArtifacts;
    private readonly ICrmUnitOfWork _uow;

    public CrmArtifactAssociationService(
        IRunArtifactLinkRepository runArtifacts,
        ICrmClientArtifactRepository clientArtifacts,
        ICrmUnitOfWork uow)
        => (_runArtifacts, _clientArtifacts, _uow) = (runArtifacts, clientArtifacts, uow);

    public async Task<int> AssociateAsync(ChainRun run, CancellationToken ct = default)
    {
        if (run.CrmClientId is not { } clientId
            || run.Status is ChainRunStatus.Running or ChainRunStatus.Waiting)
            return 0;

        var added = 0;
        foreach (var runId in run.Steps.Select(step => step.RunId).OfType<Guid>().Distinct())
        {
            foreach (var artifact in await _runArtifacts.ListForRunAsync(runId, ct))
            {
                if (await _clientArtifacts.ExistsForSourceAsync(clientId, artifact.Id, ct)) continue;
                await _clientArtifacts.AddAsync(CrmClientArtifact.CreateFromRunArtifact(
                    run.ProjectId, clientId, artifact.Id, run.Id, artifact.Title,
                    artifact.Bucket, artifact.ObjectKey, artifact.ContentType,
                    artifact.SizeBytes, artifact.CreatedAt), ct);
                added++;
            }
        }

        if (added > 0) await _uow.SaveChangesAsync(ct);
        return added;
    }

    public async Task OnCompletedAsync(
        ChainRun run,
        CancellationToken cancellationToken = default) =>
        _ = await AssociateAsync(run, cancellationToken);
}
