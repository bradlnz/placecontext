namespace PlaceContext.Application.Ports;

public sealed record AgentTokenInfo(
    Guid Id,
    string TokenPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt);
