namespace PlaceContext.Application.Ports;

public sealed record AgentToken(Guid Id, Guid TenantId, string TokenPrefix, DateTimeOffset ExpiresAt);
