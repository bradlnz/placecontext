using System.Collections.Concurrent;

namespace PlaceContext.Host.Auth;

/// <summary>A short-lived authorization code bound to a user, tenant, role, client, and PKCE challenge.</summary>
public sealed record AuthCode(
    string Code, string ClientId, string RedirectUri, string CodeChallenge,
    Guid UserId, Guid TenantId, string Role, string Scope, DateTimeOffset Expires);
