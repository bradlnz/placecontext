using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of append-only token-usage records (for the cost dashboards).</summary>
public interface IUsageRepository
{
    Task AddAsync(UsageRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<UsageRecord>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default);
    Task<IReadOnlyList<UsageRecord>> ListAllAsync(CancellationToken ct = default);
}
