using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

public interface ICrmUserRepository
{
    Task AddAsync(CrmUser user, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<CrmUser?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CrmUser?> GetByAuthUserIdAsync(Guid authUserId, CancellationToken ct = default);
    Task<CrmUser?> GetByEmailAsync(Guid projectId, string email, CancellationToken ct = default);
    Task<CrmUser?> GetByJoinCodeAsync(string joinCode, DateTimeOffset now, CancellationToken ct = default);
    Task<bool> MarkOnboardedByJoinCodeAsync(string joinCode, Guid authUserId, DateTimeOffset now, CancellationToken ct = default);
    Task<IReadOnlyList<CrmUser>> ListForProjectAsync(Guid projectId, CancellationToken ct = default);
}
