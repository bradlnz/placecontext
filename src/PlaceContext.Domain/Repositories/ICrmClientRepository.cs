using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

public interface ICrmClientRepository
{
    Task AddAsync(CrmClient client, CancellationToken ct = default);
    Task UpdateAsync(CrmClient client, CancellationToken ct = default);
    Task DeleteAsync(Guid clientId, CancellationToken ct = default);
    Task<CrmClient?> GetByIdAsync(Guid clientId, CancellationToken ct = default);
    Task<IReadOnlyList<CrmClient>> ListForProjectAsync(Guid projectId, CancellationToken ct = default);
}
