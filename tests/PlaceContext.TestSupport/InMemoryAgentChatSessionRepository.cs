using System.Collections.Concurrent;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.TestSupport;

public sealed class InMemoryAgentChatSessionRepository : IAgentChatSessionRepository
{
    private readonly ConcurrentDictionary<Guid, AgentChatSession> _store = new();

    public Task AddAsync(AgentChatSession session, CancellationToken ct = default)
    {
        _store[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AgentChatSession session, CancellationToken ct = default)
    {
        _store[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task<AgentChatSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(sessionId));

    public Task<IReadOnlyList<AgentChatSession>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AgentChatSession>>(
            _store.Values.Where(s => s.ProjectId == projectId).OrderByDescending(s => s.UpdatedAt).ToList());

    public Task<IReadOnlyList<AgentChatSession>> ListForUserAsync(Guid projectId, Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AgentChatSession>>(
            _store.Values.Where(s => s.ProjectId == projectId && s.UserId == userId)
                .OrderByDescending(s => s.UpdatedAt).ToList());
}
