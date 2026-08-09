using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Integration;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteCrmClientHandler : ICommandHandler<DeleteCrmClientCommand, bool>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICrmClientArtifactRepository _artifacts;
    private readonly ICrmArtifactsClient _storage;
    private readonly ICrmUnitOfWork _uow;

    public DeleteCrmClientHandler(
        ICrmClientRepository clients,
        ICrmClientArtifactRepository artifacts,
        ICrmArtifactsClient storage,
        ICrmUnitOfWork uow)
        => (_clients, _artifacts, _storage, _uow) = (clients, artifacts, storage, uow);

    public async Task<bool> HandleAsync(DeleteCrmClientCommand command, CancellationToken ct = default)
    {
        if (await _clients.GetByIdAsync(command.ClientId, ct) is null) return false;
        foreach (var artifact in await _artifacts.ListForClientAsync(command.ClientId, 1000, ct))
            if (artifact.IsDirectUpload)
                await _storage.DeleteAsync(artifact.Bucket, artifact.ObjectKey, ct);
        await _clients.DeleteAsync(command.ClientId, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
