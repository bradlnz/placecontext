using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

public interface ICrmClientArtifactRepository
{
    Task AddAsync(CrmClientArtifact artifact, CancellationToken ct = default);
    Task<CrmClientArtifact?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsForSourceAsync(Guid clientId, Guid sourceArtifactId, CancellationToken ct = default);
    Task<IReadOnlyList<CrmClientArtifact>> ListForClientAsync(
        Guid clientId,
        int take = 200,
        CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
