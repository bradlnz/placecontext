using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

public interface ICrmJobRunRepository
{
    Task AddAsync(CrmJobRun run, CancellationToken ct = default);
    Task<IReadOnlyList<CrmJobRun>> ListForClientAsync(Guid clientId, int take = 20, CancellationToken ct = default);
}
