using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

public interface ICrmChainRunRepository
{
    Task AddAsync(CrmChainRun run, CancellationToken ct = default);
    Task<IReadOnlyList<CrmChainRun>> ListForClientAsync(
        Guid clientId,
        int take = 20,
        CancellationToken ct = default);
}
