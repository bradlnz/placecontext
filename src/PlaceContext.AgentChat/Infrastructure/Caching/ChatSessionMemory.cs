namespace PlaceContext.AgentChat.Infrastructure.Caching;

/// <summary>Full conversation memory for a session.</summary>
public sealed record ChatSessionMemory(
    Guid Id,
    Guid ProjectId,
    string Title,
    List<ChatMemoryMessage> Messages,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastMessageAt);
