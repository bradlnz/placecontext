using PlaceContext.Application.Ports;

namespace PlaceContext.AgentChat.Infrastructure.Caching;

/// <summary>
/// Implements the Application-layer agent-session port over the Redis-backed
/// <see cref="IChatMemoryStore"/>, so launchpad agent sessions live in the same store the /chat
/// page lists and renders. Records map 1:1 (the store's model adds optional attachment fields,
/// which launchpad sessions never set).
/// </summary>
public sealed class ChatMemoryAgentSessionStore : IAgentSessionStore
{
    private readonly IChatMemoryStore _inner;

    public ChatMemoryAgentSessionStore(IChatMemoryStore inner) => _inner = inner;

    public async Task<AgentSessionMemory?> GetSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var m = await _inner.GetSessionAsync(sessionId, ct);
        return m is null ? null : new AgentSessionMemory(
            m.Id, m.ProjectId, m.Title,
            m.Messages.Select(msg => new AgentSessionMessage(
                msg.Role, msg.Content, msg.Timestamp,
                msg.ToolCalls?.Select(tc => new AgentSessionToolCall(
                    tc.ToolName, tc.Args, tc.Status, tc.Result, tc.ResultType)).ToList())).ToList(),
            m.CreatedAt, m.LastMessageAt);
    }

    public Task SaveSessionAsync(Guid sessionId, AgentSessionMemory memory, CancellationToken ct = default)
        => _inner.SaveSessionAsync(sessionId, new ChatSessionMemory(
            memory.Id, memory.ProjectId, memory.Title,
            memory.Messages.Select(msg => new ChatMemoryMessage(
                msg.Role, msg.Content, msg.Timestamp,
                msg.ToolCalls?.Select(tc => new ChatMemoryToolCall(
                    tc.ToolName, tc.Args, tc.Status, tc.Result, tc.ResultType)).ToList())).ToList(),
            memory.CreatedAt, memory.LastMessageAt), ct);
}
