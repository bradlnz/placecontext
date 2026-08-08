namespace PlaceContext.Application.Dtos;

/// <summary>Read model for a chat session.</summary>
public sealed record AgentChatSessionView(
    Guid Id,
    Guid ProjectId,
    Guid? UserId,
    string? Title,
    IReadOnlyList<AgentMessageView> Messages,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
