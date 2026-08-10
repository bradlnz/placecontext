using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

public interface IAgentDefinitionRepository
{
    Task<AgentDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AgentDefinition?> GetCommandAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentDefinition>> ListForProjectAsync(Guid projectId, CancellationToken ct = default);
    Task AddAsync(AgentDefinition agent, CancellationToken ct = default);
    Task UpdateAsync(AgentDefinition agent, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
