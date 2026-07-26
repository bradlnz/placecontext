namespace PlaceContext.Infrastructure.Caching;

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

/// <summary>Summary of a chat session for the session list sidebar.</summary>
public sealed record ChatSessionSummary(
    Guid Id,
    Guid ProjectId,
    string Title,
    int MessageCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastMessageAt);

/// <summary>Full conversation memory for a session.</summary>
public sealed record ChatSessionMemory(
    Guid Id,
    Guid ProjectId,
    string Title,
    List<ChatMemoryMessage> Messages,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastMessageAt);

/// <summary>One message in the conversation memory.</summary>
public sealed record ChatMemoryMessage(
    string Role,
    string Content,
    DateTimeOffset Timestamp,
    List<ChatMemoryToolCall>? ToolCalls = null,
    string? AttachmentName = null,
    string? AttachmentKey = null,
    string? AttachmentContentType = null,
    long AttachmentSizeBytes = 0,
    string? Thinking = null);

/// <summary>A tool call within a message.</summary>
public sealed record ChatMemoryToolCall(
    string ToolName,
    string Args,
    string Status,
    string? Result = null,
    string ResultType = "text");
