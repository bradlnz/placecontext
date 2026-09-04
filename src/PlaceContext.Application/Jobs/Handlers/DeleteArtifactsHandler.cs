using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteArtifactsHandler : ICommandHandler<DeleteArtifactsCommand, int>
{
    private readonly IRunArtifactLinkRepository _links;
    private readonly IObjectStore _store;
    private readonly IUnitOfWork _uow;

    public DeleteArtifactsHandler(IRunArtifactLinkRepository links, IObjectStore store, IUnitOfWork uow)
    {
        _links = links;
        _store = store;
        _uow = uow;
    }

    public async Task<int> HandleAsync(DeleteArtifactsCommand command, CancellationToken ct = default)
    {
        // Resolve the (tenant-scoped) links first so we know which object bytes to reap after commit.
        var found = new List<Domain.Entities.RunArtifactLink>();
        foreach (var id in command.ArtifactIds.Distinct())
        {
            var link = await _links.GetByIdAsync(id, ct);
            if (link is null) continue;
            found.Add(link);
            await _links.RemoveAsync(id, ct);
        }

        if (found.Count == 0) return 0;

        // One transaction drops every pointer, then the bytes are reaped best-effort.
        await _uow.SaveChangesAsync(ct);
        foreach (var link in found)
        {
            try { await _store.DeleteAsync(link.Bucket, link.ObjectKey, ct); }
            catch { /* orphaned object bytes are harmless — the link is already gone */ }
        }

        return found.Count;
    }
}
