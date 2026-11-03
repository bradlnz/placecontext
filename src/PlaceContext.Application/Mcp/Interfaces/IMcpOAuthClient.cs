namespace PlaceContext.Application.Mcp;

public interface IMcpOAuthClient
{
    Task<OAuthMetadata?> DiscoverMetadataAsync(string endpointUrl, CancellationToken ct);
    Task<string> RegisterClientAsync(string registrationEndpoint, string callbackUrl, CancellationToken ct);
    Task<TokenResponse> ExchangeCodeAsync(string tokenEndpoint, string code, string redirectUri, string clientId, string codeVerifier, CancellationToken ct);
    Task<TokenResponse> RefreshTokenAsync(string tokenEndpoint, string refreshToken, string clientId, CancellationToken ct);
}
