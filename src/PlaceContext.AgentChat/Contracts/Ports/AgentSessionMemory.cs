namespace PlaceContext.Application.Ports;

/// <summary>Full conversation memory for a session.</summary>
public sealed record AgentSessionMemory(
    Guid Id,
    Guid ProjectId,
    string Title,
    List<AgentSessionMessage> Messages,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastMessageAt);
