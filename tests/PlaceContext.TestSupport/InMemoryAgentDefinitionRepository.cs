using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.TestSupport;

public sealed class InMemoryAgentDefinitionRepository : IAgentDefinitionRepository
{
    private readonly List<AgentDefinition> _items = [];

    public Task<AgentDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_items.FirstOrDefault(item => item.Id == id));

    public Task<AgentDefinition?> GetCommandAsync(Guid projectId, CancellationToken ct = default)
        => Task.FromResult(_items.FirstOrDefault(item => item.ProjectId == projectId && item.Kind == AgentKind.Command));

    public Task<IReadOnlyList<AgentDefinition>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AgentDefinition>>(_items.Where(item => item.ProjectId == projectId).ToArray());

    public Task AddAsync(AgentDefinition agent, CancellationToken ct = default)
    {
        _items.Add(agent);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AgentDefinition agent, CancellationToken ct = default) => Task.CompletedTask;

    public Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        _items.RemoveAll(item => item.Id == id);
        return Task.CompletedTask;
    }
}
