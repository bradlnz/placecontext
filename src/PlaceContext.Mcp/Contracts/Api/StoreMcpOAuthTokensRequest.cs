namespace PlaceContext.Mcp.Contracts.Api;

public sealed record StoreMcpOAuthTokensRequest(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt,
    string? ClientId,
    string Status);
