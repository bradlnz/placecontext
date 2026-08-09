namespace PlaceContext.Identity.OAuth;

/// <summary>Identity-local wire request for MCP OAuth token persistence.</summary>
public sealed record StoreMcpOAuthTokensRequest(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt,
    string? ClientId,
    string Status);
