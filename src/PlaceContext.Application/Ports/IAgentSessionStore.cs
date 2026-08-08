namespace PlaceContext.Application.Ports;

/// <summary>
/// Persistence port for agent (launchpad) chat session memory. Mirrors the Save/Get shape of
/// Infrastructure's <c>IChatMemoryStore</c> (same record layout, so sessions render in /chat);
/// Application cannot reference Infrastructure, so this port is implemented there by an adapter
/// over the Redis-backed store.
/// </summary>
public interface IAgentSessionStore
{
    Task<AgentSessionMemory?> GetSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task SaveSessionAsync(Guid sessionId, AgentSessionMemory memory, CancellationToken ct = default);
}
