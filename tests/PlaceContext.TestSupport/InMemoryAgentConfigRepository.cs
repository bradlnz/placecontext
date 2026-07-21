using System.Collections.Concurrent;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.TestSupport;

public sealed class InMemoryAgentConfigRepository : IAgentConfigRepository
{
    private readonly ConcurrentDictionary<Guid, AgentConfig> _store = new();

    public Task AddAsync(AgentConfig config, CancellationToken ct = default)
    {
        _store[config.Id] = config;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AgentConfig config, CancellationToken ct = default)
    {
        _store[config.Id] = config;
        return Task.CompletedTask;
    }

    public Task<AgentConfig?> GetByProjectIdAsync(Guid projectId, CancellationToken ct = default)
        => Task.FromResult(_store.Values.FirstOrDefault(c => c.ProjectId == projectId));
}
