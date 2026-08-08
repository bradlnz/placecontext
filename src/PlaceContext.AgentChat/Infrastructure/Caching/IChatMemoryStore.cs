namespace PlaceContext.AgentChat.Infrastructure.Caching;

/// <summary>
/// Distributed memory store for chat sessions. Stores conversation context in Redis so each
/// session has persistent memory across page loads and server restarts.
/// </summary>
public interface IChatMemoryStore
{
    Task<IReadOnlyList<ChatSessionSummary>> ListSessionsAsync(Guid projectId, CancellationToken ct = default);
    Task<ChatSessionMemory?> GetSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task SaveSessionAsync(Guid sessionId, ChatSessionMemory memory, CancellationToken ct = default);
    Task DeleteSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task ClearSessionMemoryAsync(Guid sessionId, CancellationToken ct = default);
}
