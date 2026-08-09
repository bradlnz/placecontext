using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PlaceContext.Identity.OAuth;

namespace PlaceContext.Identity.Controllers;

/// <summary>OAuth 2.1 authorization-code + PKCE broker for MCP connections.</summary>
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public sealed class McpOAuthController(
    IMcpOAuthConnectionClient connections,
    IDataProtectionProvider dataProtection,
    IHttpClientFactory httpClientFactory,
    ILogger<McpOAuthController> log) : ControllerBase
{
    private const string StateCookie = "mcp.oauth.state";
    private const string OAuthAuthType = "oauth";
    private const string StateProtectionPurpose = "placecontext.identity.mcp-oauth.state.v1";
    private static readonly TimeSpan StateCookieLifetime = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [HttpGet("/mcp-oauth/start")]
    public async Task<IActionResult> Start([FromQuery] Guid connectionId, CancellationToken ct)
    {
        var tenant = GetTenantFromUser();
        if (tenant is null)
            return Unauthorized();

        var connection = await connections.GetAsync(connectionId, tenant, ct);
        if (connection is null)
            return NotFound("MCP connection not found.");
        if (string.IsNullOrWhiteSpace(connection.EndpointUrl))
            return BadRequest("Endpoint URL required.");

        McpOAuthMetadata? metadata;
        try
        {
            metadata = await DiscoverMetadataAsync(connection.EndpointUrl, ct);
        }
        catch (Exception exception)
        {
            log.LogWarning(exception, "Failed to discover OAuth metadata for {Url}", connection.EndpointUrl);
            return BadRequest($"Failed to discover OAuth metadata: {exception.Message}");
        }

        if (metadata is null
            || string.IsNullOrWhiteSpace(metadata.AuthorizationEndpoint)
            || string.IsNullOrWhiteSpace(metadata.TokenEndpoint))
            return BadRequest("External server does not expose OAuth authorization/token endpoints.");

        var clientId = connection.OAuthClientId;
        if (string.IsNullOrWhiteSpace(clientId)
            && !string.IsNullOrWhiteSpace(metadata.RegistrationEndpoint))
        {
            try
            {
                clientId = await RegisterClientAsync(metadata.RegistrationEndpoint, ct);
            }
            catch (Exception exception)
            {
                log.LogWarning(exception, "MCP dynamic client registration failed at {Url}", metadata.RegistrationEndpoint);
                return BadRequest($"Dynamic Client Registration failed: {exception.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(clientId))
            return BadRequest("No client_id is configured and the external server does not support dynamic registration.");

        var codeVerifier = ToBase64Url(RandomNumberGenerator.GetBytes(32));
        var codeChallenge = ToBase64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var callbackUrl = CallbackUrl();
        var payload = new McpOAuthState
        {
            ConnectionId = connectionId,
            CodeVerifier = codeVerifier,
            ClientId = clientId,
            TokenEndpoint = metadata.TokenEndpoint,
            CallbackUrl = callbackUrl,
            State = state,
            TenantId = tenant.TenantId,
            TenantSlug = tenant.TenantSlug,
            TenantTimeZone = tenant.TenantTimeZone,
            UserId = tenant.UserId,
        };
        var protectedState = dataProtection.CreateProtector(StateProtectionPurpose)
            .Protect(JsonSerializer.Serialize(payload));
        Response.Cookies.Append(StateCookie, protectedState, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = StateCookieLifetime,
            Path = "/",
        });

        var separator = metadata.AuthorizationEndpoint.Contains('?') ? "&" : "?";
        var authorizationUrl = $"{metadata.AuthorizationEndpoint}{separator}"
            + "response_type=code"
            + $"&client_id={Uri.EscapeDataString(clientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}"
            + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
            + "&code_challenge_method=S256"
            + $"&state={Uri.EscapeDataString(state)}"
            + (string.IsNullOrWhiteSpace(connection.OAuthScopes)
                ? ""
                : $"&scope={Uri.EscapeDataString(connection.OAuthScopes)}");
        return Redirect(authorizationUrl);
    }

    [AllowAnonymous]
    [HttpGet("/mcp-oauth/callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return ReturnToOpener($"OAuth error: {error}", false);
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return ReturnToOpener("Missing authorization code or state.", false);
        if (!Request.Cookies.TryGetValue(StateCookie, out var protectedState)
            || string.IsNullOrWhiteSpace(protectedState))
            return ReturnToOpener("OAuth session expired. Please try again.", false);

        McpOAuthState oauthState;
        try
        {
            var stateJson = dataProtection.CreateProtector(StateProtectionPurpose).Unprotect(protectedState);
            oauthState = JsonSerializer.Deserialize<McpOAuthState>(stateJson, Json)
                ?? throw new InvalidOperationException("The OAuth state is empty.");
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or InvalidOperationException)
        {
            log.LogWarning(exception, "Rejected an invalid MCP OAuth state cookie");
            return ReturnToOpener("Invalid OAuth session.", false);
        }

        if (string.IsNullOrWhiteSpace(oauthState.State)
            || !FixedTimeEquals(state, oauthState.State))
            return ReturnToOpener("State mismatch — possible CSRF attack.", false);

        var tenant = TenantFromState(oauthState);
        try
        {
            var token = await ExchangeCodeAsync(
                oauthState.TokenEndpoint,
                code,
                oauthState.CallbackUrl,
                oauthState.ClientId,
                oauthState.CodeVerifier,
                ct);
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            await connections.StoreTokensAsync(
                oauthState.ConnectionId,
                new StoreMcpOAuthTokensRequest(
                    token.AccessToken,
                    token.RefreshToken,
                    expiresAt,
                    oauthState.ClientId,
                    "oauth:connected"),
                tenant,
                ct);
            var connection = await connections.GetAsync(oauthState.ConnectionId, tenant, ct);
            Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/" });
            return ReturnToOpener($"Connected to {connection?.Name ?? "MCP server"}", true);
        }
        catch (Exception exception)
        {
            log.LogError(exception, "MCP OAuth token exchange failed for {ConnectionId}", oauthState.ConnectionId);
            return ReturnToOpener($"Token exchange failed: {exception.Message}", false);
        }
    }

    [HttpPost("/mcp-oauth/refresh")]
    public async Task<IActionResult> Refresh([FromQuery] Guid connectionId, CancellationToken ct)
    {
        var tenant = GetTenantFromUser();
        if (tenant is null)
            return Unauthorized();
        var result = await RefreshCoreAsync(connectionId, tenant, ct);
        return result.Success
            ? Ok(new { accessToken = result.AccessToken, expiresAt = result.ExpiresAt })
            : BadRequest(result.Error);
    }

    [HttpGet("/mcp-oauth/token")]
    public async Task<IActionResult> GetToken([FromQuery] Guid connectionId, CancellationToken ct)
    {
        var tenant = GetTenantFromUser();
        if (tenant is null)
            return Unauthorized();
        var connection = await connections.GetAsync(connectionId, tenant, ct);
        if (connection is null
            || connection.AuthType != OAuthAuthType
            || string.IsNullOrWhiteSpace(connection.OAuthAccessToken))
            return NotFound();

        if (connection.OAuthTokenExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            var refresh = await RefreshCoreAsync(connectionId, tenant, ct);
            if (!refresh.Success)
                return Unauthorized(new { error = "Token expired and refresh failed." });
            return Ok(new { accessToken = refresh.AccessToken, expiresAt = refresh.ExpiresAt });
        }

        return Ok(new { accessToken = connection.OAuthAccessToken, expiresAt = connection.OAuthTokenExpiresAt });
    }

    private async Task<(bool Success, string? AccessToken, DateTimeOffset? ExpiresAt, string? Error)>
        RefreshCoreAsync(Guid connectionId, IdentityTenantContext tenant, CancellationToken ct)
    {
        var connection = await connections.GetAsync(connectionId, tenant, ct);
        if (connection is null)
            return (false, null, null, "MCP connection not found.");
        if (connection.AuthType != OAuthAuthType)
            return (false, null, null, "Not an OAuth connection.");
        if (string.IsNullOrWhiteSpace(connection.OAuthRefreshToken))
            return (false, null, null, "No refresh token available. Please re-authenticate.");
        if (string.IsNullOrWhiteSpace(connection.EndpointUrl))
            return (false, null, null, "Endpoint URL required.");

        try
        {
            var metadata = await DiscoverMetadataAsync(connection.EndpointUrl, ct);
            if (string.IsNullOrWhiteSpace(metadata?.TokenEndpoint))
                return (false, null, null, "External server does not expose a token endpoint.");
            var token = await RefreshTokenAsync(
                metadata.TokenEndpoint,
                connection.OAuthRefreshToken,
                connection.OAuthClientId ?? "",
                ct);
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            await connections.StoreTokensAsync(
                connectionId,
                new StoreMcpOAuthTokensRequest(
                    token.AccessToken,
                    token.RefreshToken,
                    expiresAt,
                    connection.OAuthClientId,
                    "oauth:connected"),
                tenant,
                ct);
            return (true, token.AccessToken, expiresAt, null);
        }
        catch (Exception exception)
        {
            log.LogError(exception, "MCP OAuth refresh failed for {ConnectionId}", connectionId);
            await connections.UpdateStatusAsync(
                connectionId,
                new UpdateMcpOAuthStatusRequest("oauth:expired"),
                tenant,
                ct);
            return (false, null, null, $"Token refresh failed: {exception.Message}");
        }
    }

    private async Task<McpOAuthMetadata?> DiscoverMetadataAsync(string endpointUrl, CancellationToken ct)
    {
        var endpoint = new Uri(endpointUrl, UriKind.Absolute);
        if (endpoint.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("The MCP endpoint must use HTTP or HTTPS.");
        var origin = endpoint.GetLeftPart(UriPartial.Authority);
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        try
        {
            using var resourceResponse = await client.GetAsync(
                $"{origin}/.well-known/oauth-protected-resource",
                ct);
            if (resourceResponse.IsSuccessStatusCode)
            {
                var resource = await resourceResponse.Content.ReadFromJsonAsync<McpProtectedResourceMetadata>(Json, ct);
                if (resource?.AuthorizationServers?.FirstOrDefault() is { } authorizationServer)
                {
                    using var metadataResponse = await client.GetAsync(
                        $"{authorizationServer.TrimEnd('/')}/.well-known/oauth-authorization-server",
                        ct);
                    if (metadataResponse.IsSuccessStatusCode)
                        return await metadataResponse.Content.ReadFromJsonAsync<McpOAuthMetadata>(Json, ct);
                }
            }
        }
        catch (Exception exception)
        {
            log.LogDebug(exception, "Protected-resource discovery failed for {Url}", endpointUrl);
        }

        using var fallback = await client.GetAsync(
            $"{origin}/.well-known/oauth-authorization-server",
            ct);
        return fallback.IsSuccessStatusCode
            ? await fallback.Content.ReadFromJsonAsync<McpOAuthMetadata>(Json, ct)
            : null;
    }

    private async Task<string> RegisterClientAsync(string registrationEndpoint, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        using var response = await client.PostAsJsonAsync(
            registrationEndpoint,
            new { redirect_uris = new[] { CallbackUrl() }, client_name = "PlaceContext" },
            ct);
        response.EnsureSuccessStatusCode();
        var registration = await response.Content.ReadFromJsonAsync<McpOAuthRegisterResponse>(Json, ct);
        return registration?.ClientId
            ?? throw new InvalidOperationException("No client_id in the dynamic registration response.");
    }

    private Task<McpOAuthTokenResponse> ExchangeCodeAsync(
        string tokenEndpoint,
        string code,
        string redirectUri,
        string clientId,
        string codeVerifier,
        CancellationToken ct)
        => SendTokenRequestAsync(tokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier,
        }, ct);

    private Task<McpOAuthTokenResponse> RefreshTokenAsync(
        string tokenEndpoint,
        string refreshToken,
        string clientId,
        CancellationToken ct)
        => SendTokenRequestAsync(tokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        }, ct);

    private async Task<McpOAuthTokenResponse> SendTokenRequestAsync(
        string tokenEndpoint,
        Dictionary<string, string> form,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        using var response = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token endpoint returned {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<McpOAuthTokenResponse>(body, Json)
            ?? throw new InvalidOperationException("Invalid token response.");
    }

    private IdentityTenantContext? GetTenantFromUser()
    {
        if (!Guid.TryParse(User.FindFirstValue("tenant"), out var tenantId)
            || !Guid.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"),
                out var userId))
            return null;
        return new IdentityTenantContext(
            tenantId,
            User.FindFirstValue("tenant_slug") ?? tenantId.ToString("N"),
            User.FindFirstValue("tenant_timezone") ?? "UTC",
            userId);
    }

    private static IdentityTenantContext TenantFromState(McpOAuthState state)
        => new(state.TenantId, state.TenantSlug, state.TenantTimeZone, state.UserId);

    private string CallbackUrl() => $"{PublicOrigin()}/mcp-oauth/callback";

    private string PublicOrigin()
    {
        var scheme = Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? Request.Scheme;
        var host = Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? Request.Host.Value;
        return $"{scheme}://{host}";
    }

    private IActionResult ReturnToOpener(string message, bool success)
    {
        var eventName = success ? "mcp-oauth-complete" : "mcp-oauth-error";
        var origin = JsonSerializer.Serialize(PublicOrigin());
        var html = $"""
            <!doctype html>
            <html><head><meta charset="utf-8"><title>MCP OAuth</title></head><body>
            <script>window.opener?.postMessage('{eventName}', {origin}); window.close();</script>
            <div style="font-family:system-ui;padding:40px;text-align:center;color:{(success ? "#43d675" : "#e5534b")}">
              <p>{System.Net.WebUtility.HtmlEncode(message)}</p>
              <p>This window should close automatically.</p>
            </div>
            </body></html>
            """;
        return Content(html, "text/html");
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
