using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of per-project <see cref="AgentConfig"/> (one per project, project-scoped).</summary>
public interface IAgentConfigRepository
{
    Task<AgentConfig?> GetByProjectIdAsync(Guid projectId, CancellationToken ct = default);
    Task AddAsync(AgentConfig config, CancellationToken ct = default);
    Task UpdateAsync(AgentConfig config, CancellationToken ct = default);
}
