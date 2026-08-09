using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Integration;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class RemoveCrmClientArtifactHandler
    : ICommandHandler<RemoveCrmClientArtifactCommand, bool>
{
    private readonly ICrmClientArtifactRepository _artifacts;
    private readonly ICrmArtifactsClient _storage;
    private readonly ICrmUnitOfWork _uow;

    public RemoveCrmClientArtifactHandler(
        ICrmClientArtifactRepository artifacts,
        ICrmArtifactsClient storage,
        ICrmUnitOfWork uow)
        => (_artifacts, _storage, _uow) = (artifacts, storage, uow);

    public async Task<bool> HandleAsync(
        RemoveCrmClientArtifactCommand command,
        CancellationToken ct = default)
    {
        var value = await _artifacts.GetByIdAsync(command.ArtifactId, ct);
        if (value is null) return false;
        if (value.IsDirectUpload) await _storage.DeleteAsync(value.Bucket, value.ObjectKey, ct);
        await _artifacts.RemoveAsync(value.Id, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
