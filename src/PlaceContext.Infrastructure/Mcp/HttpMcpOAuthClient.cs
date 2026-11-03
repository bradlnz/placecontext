using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Mcp;

namespace PlaceContext.Infrastructure.Mcp;

public sealed class HttpMcpOAuthClient(IHttpClientFactory _http, ILogger<HttpMcpOAuthClient> _log) : IMcpOAuthClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public async Task<OAuthMetadata?> DiscoverMetadataAsync(string endpointUrl, CancellationToken ct)
    {
        // Strip trailing path to get the base URL
        var uri = new Uri(endpointUrl);
        var baseUri = $"{uri.Scheme}://{uri.Host}:{uri.Port}";

        using var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        // Try RFC 9728 protected resource metadata first
        try
        {
            using var resourceResp = await client.GetAsync($"{baseUri}/.well-known/oauth-protected-resource", ct);
            if (resourceResp.IsSuccessStatusCode)
            {
                var resourceJson = await resourceResp.Content.ReadAsStringAsync(ct);
                var resource = JsonSerializer.Deserialize<ProtectedResourceMetadata>(resourceJson, Json);
                if (resource?.AuthorizationServers?.Length > 0)
                {
                    var authServer = resource.AuthorizationServers[0];
                    using var metaResp = await client.GetAsync($"{authServer.TrimEnd('/')}/.well-known/oauth-authorization-server", ct);
                    if (metaResp.IsSuccessStatusCode)
                    {
                        var metaJson = await metaResp.Content.ReadAsStringAsync(ct);
                        return JsonSerializer.Deserialize<OAuthMetadata>(metaJson, Json);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "RFC 9728 discovery failed for {Url}, trying direct", endpointUrl);
        }

        // Fallback: try the endpoint URL directly
        try
        {
            using var metaResp = await client.GetAsync($"{baseUri}/.well-known/oauth-authorization-server", ct);
            if (metaResp.IsSuccessStatusCode)
            {
                var metaJson = await metaResp.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<OAuthMetadata>(metaJson, Json);
            }
        }
        catch { }

        return null;
    }

    public async Task<string> RegisterClientAsync(string registrationEndpoint, string callbackUrl, CancellationToken ct)
    {
        using var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);


        var body = JsonSerializer.Serialize(new
        {
            redirect_uris = new[] { callbackUrl },
            client_name = "PlaceContext",
        });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await client.PostAsync(registrationEndpoint, content, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<RegisterResponse>(json, Json);
        return result?.ClientId ?? throw new InvalidOperationException("No client_id in DCR response.");
    }

    public async Task<TokenResponse> ExchangeCodeAsync(
        string tokenEndpoint, string code, string redirectUri,
        string clientId, string codeVerifier, CancellationToken ct)
    {
        using var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier,
        };
        using var resp = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token endpoint returned {(int)resp.StatusCode}: {json}");
        }
        return JsonSerializer.Deserialize<TokenResponse>(json, Json)
            ?? throw new InvalidOperationException("Invalid token response.");
    }

    public async Task<TokenResponse> RefreshTokenAsync(
        string tokenEndpoint, string refreshToken, string clientId, CancellationToken ct)
    {
        using var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        };
        using var resp = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token refresh failed ({(int)resp.StatusCode}): {json}");
        }
        return JsonSerializer.Deserialize<TokenResponse>(json, Json)
            ?? throw new InvalidOperationException("Invalid token response.");
    }

    private sealed class ProtectedResourceMetadata
    {
        [JsonPropertyName("authorization_servers")]
        public string[]? AuthorizationServers
        {
            get; set;
        }
    }

    private sealed class RegisterResponse
    {
        [JsonPropertyName("client_id")]
        public string? ClientId
        {
            get; set;
        }
    }

}
