using PlaceContext.Application.Secrets;
using PlaceContext.Application.Shared;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Mcp;

public sealed class McpOAuthService(IMcpConnectionRepository connections, IUnitOfWork uow,
    IDataEncryptor encryptor, IMcpOAuthClient client, IClock clock)
{
    private const string Purpose = "mcp.oauth.tokens";

    public async Task<McpOAuthConnection?> GetConnectionAsync(Guid id, CancellationToken ct)
    {
        var connection = await connections.GetByIdAsync(id, ct);
        return connection is null ? null : new(connection.Id, connection.Name, connection.EndpointUrl,
            connection.OAuthClientId, connection.OAuthScopes, connection.AuthType == McpAuthType.OAuth,
            !string.IsNullOrEmpty(connection.OAuthRefreshToken), !string.IsNullOrEmpty(connection.OAuthAccessToken), connection.OAuthTokenExpired);
    }

    public Task<OAuthMetadata?> DiscoverMetadataAsync(string endpoint, CancellationToken ct)
        => client.DiscoverMetadataAsync(endpoint, ct);
    public Task<string> RegisterClientAsync(string endpoint, string callbackUrl, CancellationToken ct)
        => client.RegisterClientAsync(endpoint, callbackUrl, ct);

    public async Task<string?> CompleteAsync(Guid id, string endpoint, string code, string callbackUrl,
        string clientId, string verifier, CancellationToken ct)
    {
        var connection = await connections.GetByIdAsync(id, ct);
        if (connection is null)
        {
            return null;
        }
        var token = await client.ExchangeCodeAsync(endpoint, code, callbackUrl, clientId, verifier, ct);
        connection.StoreOAuthTokens(encryptor.Protect(token.AccessToken, Purpose),
            string.IsNullOrEmpty(token.RefreshToken) ? null : encryptor.Protect(token.RefreshToken, Purpose),
            clock.UtcNow.AddSeconds(token.ExpiresIn), clock.UtcNow);
        if (!string.IsNullOrEmpty(clientId))
        {
            connection.SetOAuthCredentials(clientId, connection.OAuthScopes, clock.UtcNow);
        }
        connection.RecordConnection("oauth:connected", clock.UtcNow);
        await connections.UpdateAsync(connection, ct);
        await uow.SaveChangesAsync(ct);
        return connection.Name;
    }

    public async Task<McpOAuthAccessToken> RefreshAsync(Guid id, CancellationToken ct)
    {
        var connection = await connections.GetByIdAsync(id, ct) ?? throw new InvalidOperationException("MCP connection not found.");
        if (connection.AuthType != McpAuthType.OAuth)
        {
            throw new ArgumentException("Not an OAuth connection.");
        }
        if (string.IsNullOrEmpty(connection.OAuthRefreshToken))
        {
            throw new ArgumentException("No refresh token available. Please re-authenticate.");
        }
        var refresh = encryptor.Unprotect(connection.OAuthRefreshToken, Purpose);
        if (string.IsNullOrEmpty(refresh))
        {
            throw new ArgumentException("Refresh token is invalid. Please re-authenticate.");
        }
        var metadata = await client.DiscoverMetadataAsync(connection.EndpointUrl!, ct);
        if (string.IsNullOrEmpty(metadata?.TokenEndpoint))
        {
            throw new InvalidOperationException("External server does not expose a token endpoint.");
        }
        try
        {
            var token = await client.RefreshTokenAsync(metadata.TokenEndpoint, refresh, connection.OAuthClientId ?? "", ct);
            var expiry = clock.UtcNow.AddSeconds(token.ExpiresIn);
            connection.StoreOAuthTokens(encryptor.Protect(token.AccessToken, Purpose),
                string.IsNullOrEmpty(token.RefreshToken) ? connection.OAuthRefreshToken : encryptor.Protect(token.RefreshToken, Purpose), expiry, clock.UtcNow);
            connection.RecordConnection("oauth:connected", clock.UtcNow);
            await connections.UpdateAsync(connection, ct);
            await uow.SaveChangesAsync(ct);
            return new(token.AccessToken, expiry);
        }
        catch
        {
            connection.RecordConnection("oauth:expired", clock.UtcNow);
            await connections.UpdateAsync(connection, ct);
            await uow.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task<McpOAuthAccessToken?> GetTokenAsync(Guid id, CancellationToken ct)
    {
        var connection = await connections.GetByIdAsync(id, ct);
        if (connection is null || connection.AuthType != McpAuthType.OAuth || string.IsNullOrEmpty(connection.OAuthAccessToken))
        {
            return null;
        }
        if (connection.OAuthTokenExpired && !string.IsNullOrEmpty(connection.OAuthRefreshToken))
        {
            return await RefreshAsync(id, ct);
        }
        return new(encryptor.Unprotect(connection.OAuthAccessToken, Purpose), connection.OAuthTokenExpiresAt);
    }
}
