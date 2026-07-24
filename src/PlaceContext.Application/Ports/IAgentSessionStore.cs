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

/// <summary>Full conversation memory for a session.</summary>
public sealed record AgentSessionMemory(
    Guid Id,
    Guid ProjectId,
    string Title,
    List<AgentSessionMessage> Messages,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastMessageAt);

/// <summary>One message in the conversation memory.</summary>
public sealed record AgentSessionMessage(
    string Role,
    string Content,
    DateTimeOffset Timestamp,
    List<AgentSessionToolCall>? ToolCalls = null);

/// <summary>A tool call within a message.</summary>
public sealed record AgentSessionToolCall(
    string ToolName,
    string Args,
    string Status,
    string? Result = null,
    string ResultType = "text");
