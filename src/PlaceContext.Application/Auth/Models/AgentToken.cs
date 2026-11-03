namespace PlaceContext.Application.Auth;

public sealed record AgentToken(Guid Id, Guid TenantId, string TokenPrefix, DateTimeOffset ExpiresAt);
