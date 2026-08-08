namespace PlaceContext.AgentChat.Infrastructure.Caching;

/// <summary>Summary of a chat session for the session list sidebar.</summary>
public sealed record ChatSessionSummary(
    Guid Id,
    Guid ProjectId,
    string Title,
    int MessageCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastMessageAt);
