namespace PlaceContext.Application.Ports;

/// <summary>A refresh grant: the identity a refresh token renews access for, plus the raw token itself.
/// The raw token exists only in flight — at rest only its hash is stored.</summary>
public sealed record OAuthRefreshGrant(
    string Token, string ClientId, Guid UserId, Guid TenantId, string Role, string Scope, DateTimeOffset ExpiresAt);
