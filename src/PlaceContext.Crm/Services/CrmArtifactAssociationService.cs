using PlaceContext.Crm.Domain.Persistence;
using PlaceContext.Crm.Integration;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Crm.Services;

/// <summary>
/// Tags every stored output from a terminal CRM chain run to its customer. Keeping this at the
/// chain boundary covers manual CRM runs, ingestion automations, and durable wait continuations.
/// </summary>
public sealed class CrmArtifactAssociationService
{
    private readonly ICrmArtifactsClient _artifacts;
    private readonly ICrmClientArtifactRepository _clientArtifacts;
    private readonly ICrmUnitOfWork _uow;

    public CrmArtifactAssociationService(
        ICrmArtifactsClient artifacts,
        ICrmClientArtifactRepository clientArtifacts,
        ICrmUnitOfWork uow)
        => (_artifacts, _clientArtifacts, _uow) = (artifacts, clientArtifacts, uow);

    public async Task<int> AssociateAsync(
        Guid projectId,
        Guid clientId,
        Guid chainRunId,
        IEnumerable<Guid> runIds,
        CancellationToken ct = default)
    {
        var added = 0;
        foreach (var runId in runIds.Distinct())
        {
            foreach (var artifact in await _artifacts.ListForRunAsync(runId, ct))
            {
                if (await _clientArtifacts.ExistsForSourceAsync(clientId, artifact.Id, ct)) continue;
                await _clientArtifacts.AddAsync(CrmClientArtifact.CreateFromRunArtifact(
                    projectId, clientId, artifact.Id, chainRunId, artifact.Title,
                    artifact.Bucket, artifact.ObjectKey, artifact.ContentType,
                    artifact.SizeBytes, artifact.CreatedAt), ct);
                added++;
            }
        }

        if (added > 0) await _uow.SaveChangesAsync(ct);
        return added;
    }
}
