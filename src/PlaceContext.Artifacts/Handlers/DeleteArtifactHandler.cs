using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteArtifactHandler : ICommandHandler<DeleteArtifactCommand, bool>
{
    private readonly IRunArtifactLinkRepository _links;
    private readonly IObjectStore _store;
    private readonly IArtifactsUnitOfWork _uow;

    public DeleteArtifactHandler(IRunArtifactLinkRepository links, IObjectStore store, IArtifactsUnitOfWork uow)
    {
        _links = links;
        _store = store;
        _uow = uow;
    }

    public async Task<bool> HandleAsync(DeleteArtifactCommand command, CancellationToken ct = default)
    {
        // GetByIdAsync respects the tenant global filter — a caller only sees its own artifacts.
        var link = await _links.GetByIdAsync(command.ArtifactId, ct);
        if (link is null) return false;

        // Drop the DB pointer first (the source of truth for what's visible), then best-effort
        // delete the bytes. If the object delete fails we've still removed the dangling link.
        await _links.RemoveAsync(command.ArtifactId, ct);
        await _uow.SaveChangesAsync(ct);

        try { await _store.DeleteAsync(link.Bucket, link.ObjectKey, ct); }
        catch { /* orphaned object bytes are harmless — the link is already gone */ }

        return true;
    }
}
