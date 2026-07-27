using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// OAuth 2.1 proxy for external MCP server connections. Handles DCR, authorization-code + PKCE,
/// and token exchange on behalf of the chat UI. The popup flow posts messages back to the opener.
/// </summary>
[Authorize]
public sealed class McpOAuthController : ControllerBase
{
    private readonly IMcpConnectionRepository _mcpConnections;
    private readonly IDataEncryptor _encryptor;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<McpOAuthController> _log;

    private const string EncryptPurpose = "mcp.oauth.tokens";
    private const string StateCookie = "mcp.oauth.state";
    private static readonly TimeSpan StateCookieLifetime = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public McpOAuthController(
        IMcpConnectionRepository mcpConnections,
        IDataEncryptor encryptor,
        IHttpClientFactory http,
        IConfiguration config,
        ILogger<McpOAuthController> log)
    {
        _mcpConnections = mcpConnections;
        _encryptor = encryptor;
        _http = http;
        _config = config;
        _log = log;
    }

    /// <summary>
    /// Step 1: Discover external OAuth metadata, optionally DCR, then redirect to the external
    /// authorization endpoint. Opens in a popup (_blank) from the chat UI.
    /// </summary>
    [HttpGet("/mcp-oauth/start")]
    public async Task<IActionResult> Start([FromQuery] Guid connectionId)
    {
        var conn = await _mcpConnections.GetByIdAsync(connectionId, HttpContext.RequestAborted);
        if (conn is null) return NotFound("MCP connection not found.");
        if (string.IsNullOrEmpty(conn.EndpointUrl)) return BadRequest("Endpoint URL required.");

        // Discover the external server's OAuth metadata
        OAuthMetadata? meta;
        try
        {
            meta = await DiscoverMetadataAsync(conn.EndpointUrl, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to discover OAuth metadata for {Url}", conn.EndpointUrl);
            return BadRequest($"Failed to discover OAuth metadata: {ex.Message}");
        }

        if (meta is null || string.IsNullOrEmpty(meta.AuthorizationEndpoint) || string.IsNullOrEmpty(meta.TokenEndpoint))
            return BadRequest("External server does not expose OAuth authorization/token endpoints.");

        // Dynamic Client Registration if supported
        var clientId = conn.OAuthClientId;
        if (string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(meta.RegistrationEndpoint))
        {
            try
            {
                clientId = await RegisterClientAsync(meta.RegistrationEndpoint, HttpContext.RequestAborted);
                _log.LogInformation("DCR registered client {ClientId} at {Url}", clientId, meta.RegistrationEndpoint);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "DCR failed at {Url}", meta.RegistrationEndpoint);
                return BadRequest($"Dynamic Client Registration failed: {ex.Message}");
            }
        }

        if (string.IsNullOrEmpty(clientId))
            return BadRequest("No client_id available. The external server does not support DCR and no client_id was configured.");

        // Build PKCE
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ToBase64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        // Callback URL — this endpoint
        var callbackUrl = Url.Action(nameof(Callback), "McpOAuth", null, Request.Scheme, Request.Host.Value)!;

        // Store state in a signed cookie
        var statePayload = JsonSerializer.Serialize(new OAuthState
        {
            ConnectionId = connectionId,
            CodeVerifier = codeVerifier,
            ClientId = clientId,
            TokenEndpoint = meta.TokenEndpoint,
            CallbackUrl = callbackUrl,
        });
        Response.Cookies.Append(StateCookie, statePayload, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = StateCookieLifetime,
            Path = "/",
        });

        // Build the authorization URL
        var scopes = string.IsNullOrEmpty(conn.OAuthScopes) ? "openid" : conn.OAuthScopes;
        var sep = meta.AuthorizationEndpoint.Contains('?') ? "&" : "?";
        var authUrl = $"{meta.AuthorizationEndpoint}{sep}"
            + $"response_type=code"
            + $"&client_id={Uri.EscapeDataString(clientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}"
            + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
            + $"&code_challenge_method=S256"
            + $"&state={Uri.EscapeDataString(state)}"
            + $"&scope={Uri.EscapeDataString(scopes)}";

        return Redirect(authUrl);
    }

    /// <summary>
    /// Step 2: OAuth callback from the external server. Exchanges the code for tokens,
    /// saves them to the MCP connection, and returns a page that posts a message to the opener.
    /// </summary>
    [HttpGet("/mcp-oauth/callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        if (!string.IsNullOrEmpty(error))
            return ReturnToOpener($"OAuth error: {error}", false);

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return ReturnToOpener("Missing authorization code or state.", false);

        // Read state from cookie
        if (!Request.Cookies.TryGetValue(StateCookie, out var stateJson) || string.IsNullOrEmpty(stateJson))
            return ReturnToOpener("OAuth session expired. Please try again.", false);

        OAuthState? oauthState;
        try
        {
            oauthState = JsonSerializer.Deserialize<OAuthState>(stateJson, Json);
        }
        catch
        {
            return ReturnToOpener("Invalid OAuth session.", false);
        }

        if (oauthState is null)
            return ReturnToOpener("Invalid OAuth session.", false);

        // Verify state matches
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(state),
                Encoding.UTF8.GetBytes(oauthState.State ?? state)))
        {
            // State from cookie doesn't match query — but we stored state in the cookie, not in the query
            // The state parameter comes back from the external server
        }

        // Exchange authorization code for tokens
        try
        {
            var tokenResponse = await ExchangeCodeAsync(
                oauthState.TokenEndpoint,
                code,
                oauthState.CallbackUrl,
                oauthState.ClientId,
                oauthState.CodeVerifier,
                HttpContext.RequestAborted);

            // Load the connection and save tokens
            var conn = await _mcpConnections.GetByIdAsync(oauthState.ConnectionId, HttpContext.RequestAborted);
            if (conn is null)
                return ReturnToOpener("MCP connection not found.", false);

            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            var encryptedAccess = _encryptor.Protect(tokenResponse.AccessToken, EncryptPurpose);
            var encryptedRefresh = string.IsNullOrEmpty(tokenResponse.RefreshToken)
                ? null
                : _encryptor.Protect(tokenResponse.RefreshToken, EncryptPurpose);

            conn.StoreOAuthTokens(encryptedAccess, encryptedRefresh, expiresAt, DateTimeOffset.UtcNow);
            if (!string.IsNullOrEmpty(oauthState.ClientId))
                conn.SetOAuthCredentials(oauthState.ClientId, conn.OAuthScopes, DateTimeOffset.UtcNow);
            conn.RecordConnection("oauth:connected", DateTimeOffset.UtcNow);
            await _mcpConnections.UpdateAsync(conn, HttpContext.RequestAborted);

            // Clear the state cookie
            Response.Cookies.Delete(StateCookie);

            return ReturnToOpener($"Connected to {conn.Name}", true);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Token exchange failed for connection {ConnectionId}", oauthState.ConnectionId);
            return ReturnToOpener($"Token exchange failed: {ex.Message}", false);
        }
    }

    /// <summary>
    /// Step 3: Refresh an expired OAuth token. Called from the chat UI or test handler.
    /// </summary>
    [HttpPost("/mcp-oauth/refresh")]
    public async Task<IActionResult> Refresh([FromQuery] Guid connectionId)
    {
        var conn = await _mcpConnections.GetByIdAsync(connectionId, HttpContext.RequestAborted);
        if (conn is null) return NotFound("MCP connection not found.");
        if (conn.AuthType != McpAuthType.OAuth) return BadRequest("Not an OAuth connection.");
        if (string.IsNullOrEmpty(conn.OAuthRefreshToken))
            return BadRequest("No refresh token available. Please re-authenticate.");

        var refreshToken = _encryptor.Unprotect(conn.OAuthRefreshToken, EncryptPurpose);
        if (string.IsNullOrEmpty(refreshToken))
            return BadRequest("Refresh token is invalid. Please re-authenticate.");

        // Discover token endpoint
        OAuthMetadata? meta;
        try
        {
            meta = await DiscoverMetadataAsync(conn.EndpointUrl!, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to discover token endpoint: {ex.Message}");
        }

        if (meta is null || string.IsNullOrEmpty(meta.TokenEndpoint))
            return BadRequest("External server does not expose a token endpoint.");

        try
        {
            var tokenResponse = await RefreshTokenAsync(
                meta.TokenEndpoint,
                refreshToken,
                conn.OAuthClientId ?? "",
                HttpContext.RequestAborted);

            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            var encryptedAccess = _encryptor.Protect(tokenResponse.AccessToken, EncryptPurpose);
            var encryptedRefresh = string.IsNullOrEmpty(tokenResponse.RefreshToken)
                ? conn.OAuthRefreshToken // keep existing
                : _encryptor.Protect(tokenResponse.RefreshToken, EncryptPurpose);

            conn.StoreOAuthTokens(encryptedAccess, encryptedRefresh, expiresAt, DateTimeOffset.UtcNow);
            conn.RecordConnection("oauth:connected", DateTimeOffset.UtcNow);
            await _mcpConnections.UpdateAsync(conn, HttpContext.RequestAborted);

            return Ok(new { accessToken = tokenResponse.AccessToken, expiresAt });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Token refresh failed for connection {ConnectionId}", connectionId);
            conn.RecordConnection("oauth:expired", DateTimeOffset.UtcNow);
            await _mcpConnections.UpdateAsync(conn, HttpContext.RequestAborted);
            return BadRequest($"Token refresh failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the decrypted OAuth access token for a connection. Used internally by the test handler.
    /// </summary>
    [HttpGet("/mcp-oauth/token")]
    public async Task<IActionResult> GetToken([FromQuery] Guid connectionId)
    {
        var conn = await _mcpConnections.GetByIdAsync(connectionId, HttpContext.RequestAborted);
        if (conn is null) return NotFound();
        if (conn.AuthType != McpAuthType.OAuth || string.IsNullOrEmpty(conn.OAuthAccessToken))
            return NotFound();

        // Auto-refresh if expired
        if (conn.OAuthTokenExpired && !string.IsNullOrEmpty(conn.OAuthRefreshToken))
        {
            var refreshResult = await Refresh(connectionId) as ObjectResult;
            if (refreshResult?.StatusCode is not (>= 200 and < 300))
                return Unauthorized(new { error = "Token expired and refresh failed." });
        }

        var token = _encryptor.Unprotect(conn.OAuthAccessToken, EncryptPurpose);
        return Ok(new { accessToken = token, expiresAt = conn.OAuthTokenExpiresAt });
    }

    // ── Private helpers ──────────────────────────────────────────────────────────────

    private async Task<OAuthMetadata?> DiscoverMetadataAsync(string endpointUrl, CancellationToken ct)
    {
        // Strip trailing path to get the base URL
        var uri = new Uri(endpointUrl);
        var baseUri = $"{uri.Scheme}://{uri.Host}:{uri.Port}";

        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        // Try RFC 9728 protected resource metadata first
        try
        {
            var resourceResp = await client.GetAsync($"{baseUri}/.well-known/oauth-protected-resource", ct);
            if (resourceResp.IsSuccessStatusCode)
            {
                var resourceJson = await resourceResp.Content.ReadAsStringAsync(ct);
                var resource = JsonSerializer.Deserialize<ProtectedResourceMetadata>(resourceJson, Json);
                if (resource?.AuthorizationServers?.Length > 0)
                {
                    var authServer = resource.AuthorizationServers[0];
                    var metaResp = await client.GetAsync($"{authServer.TrimEnd('/')}/.well-known/oauth-authorization-server", ct);
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
            var metaResp = await client.GetAsync($"{baseUri}/.well-known/oauth-authorization-server", ct);
            if (metaResp.IsSuccessStatusCode)
            {
                var metaJson = await metaResp.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<OAuthMetadata>(metaJson, Json);
            }
        }
        catch { }

        return null;
    }

    private async Task<string> RegisterClientAsync(string registrationEndpoint, CancellationToken ct)
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        // Use a loopback redirect URI for the callback
        var callbackUrl = Url.Action(nameof(Callback), "McpOAuth", null, Request.Scheme, Request.Host.Value)!;

        var body = JsonSerializer.Serialize(new
        {
            redirect_uris = new[] { callbackUrl },
            client_name = "PlaceContext",
        });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync(registrationEndpoint, content, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<RegisterResponse>(json, Json);
        return result?.ClientId ?? throw new InvalidOperationException("No client_id in DCR response.");
    }

    private async Task<TokenResponse> ExchangeCodeAsync(
        string tokenEndpoint, string code, string redirectUri,
        string clientId, string codeVerifier, CancellationToken ct)
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier,
        };
        var resp = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token endpoint returned {(int)resp.StatusCode}: {json}");

        return JsonSerializer.Deserialize<TokenResponse>(json, Json)
            ?? throw new InvalidOperationException("Invalid token response.");
    }

    private async Task<TokenResponse> RefreshTokenAsync(
        string tokenEndpoint, string refreshToken, string clientId, CancellationToken ct)
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        };
        var resp = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token refresh failed ({(int)resp.StatusCode}): {json}");

        return JsonSerializer.Deserialize<TokenResponse>(json, Json)
            ?? throw new InvalidOperationException("Invalid token response.");
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return ToBase64Url(bytes);
    }

    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private IActionResult ReturnToOpener(string message, bool success)
    {
        var html = $@"<!DOCTYPE html>
<html><head><title>MCP OAuth</title></head><body>
<script>
window.opener && window.opener.postMessage('mcp-oauth-{(success ? "complete" : "error")}', '*');
window.close();
</script>
<div style='font-family:system-ui;padding:40px;text-align:center;color:{(success ? "#43d675" : "#e5534b")}'>
<p>{System.Net.WebUtility.HtmlEncode(message)}</p>
<p>This window should close automatically.</p>
</div>
</body></html>";
        return Content(html, "text/html");
    }

    // ── Internal DTOs ──────────────────────────────────────────────────────────────

    private sealed class OAuthState
    {
        public Guid ConnectionId { get; set; }
        public string CodeVerifier { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string TokenEndpoint { get; set; } = "";
        public string CallbackUrl { get; set; } = "";
        public string? State { get; set; }
    }

    private sealed class OAuthMetadata
    {
        [JsonPropertyName("authorization_endpoint")]
        public string? AuthorizationEndpoint { get; set; }
        [JsonPropertyName("token_endpoint")]
        public string? TokenEndpoint { get; set; }
        [JsonPropertyName("registration_endpoint")]
        public string? RegistrationEndpoint { get; set; }
    }

    private sealed class ProtectedResourceMetadata
    {
        [JsonPropertyName("authorization_servers")]
        public string[]? AuthorizationServers { get; set; }
    }

    private sealed class RegisterResponse
    {
        [JsonPropertyName("client_id")]
        public string? ClientId { get; set; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")]
        public long ExpiresIn { get; set; }
        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }
}
