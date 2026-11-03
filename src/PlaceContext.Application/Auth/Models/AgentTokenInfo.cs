namespace PlaceContext.Application.Auth;

public sealed record AgentTokenInfo(
    Guid Id,
    string TokenPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt);
