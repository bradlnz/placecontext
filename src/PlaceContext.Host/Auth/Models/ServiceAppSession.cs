namespace PlaceContext.Host.Auth;

public sealed record ServiceAppSession(Guid TenantId, Guid UserId, string Role, Guid ProjectId, Guid ServiceId, DateTimeOffset ExpiresAt);
