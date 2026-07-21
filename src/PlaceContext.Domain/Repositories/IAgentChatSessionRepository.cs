using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of <see cref="AgentChatSession"/> (project-scoped, per-user).</summary>
public interface IAgentChatSessionRepository
{
    Task AddAsync(AgentChatSession session, CancellationToken ct = default);
    Task UpdateAsync(AgentChatSession session, CancellationToken ct = default);
    Task<AgentChatSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentChatSession>> ListForProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentChatSession>> ListForUserAsync(Guid projectId, Guid userId, CancellationToken ct = default);
}
