using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Access.Models;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// OAuth 2.1 proxy for external MCP server connections. Handles DCR, authorization-code + PKCE,
/// and token exchange on behalf of the chat UI. The popup flow posts messages back to the opener.
/// </summary>
[Authorize(Policy = Permission.SecretsManage)]
public sealed class McpOAuthController : ControllerBase
{
    private readonly McpOAuthService _oauth;
    private readonly ITimeLimitedDataProtector _stateProtector;
    private readonly ILogger<McpOAuthController> _log;
    private const string StateCookie = "mcp.oauth.state";
    private const string StatePurpose = "mcp.oauth.state.v1";
    private static readonly TimeSpan StateCookieLifetime = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public McpOAuthController(McpOAuthService oauth, IDataProtectionProvider protection, ILogger<McpOAuthController> log)
        => (_oauth, _stateProtector, _log) = (oauth, protection.CreateProtector(StatePurpose).ToTimeLimitedDataProtector(), log);

    /// <summary>
    /// Step 1: Discover external OAuth metadata, optionally DCR, then redirect to the external
    /// authorization endpoint. Opens in a popup (_blank) from the chat UI.
    /// </summary>
    [HttpGet("/mcp-oauth/start")]
    public async Task<IActionResult> Start([FromQuery] Guid connectionId)
    {
        var conn = await _oauth.GetConnectionAsync(connectionId, HttpContext.RequestAborted);
        if (conn is null)
        {
            return NotFound("MCP connection not found.");
        }
        if (string.IsNullOrEmpty(conn.EndpointUrl))
        {
            return BadRequest("Endpoint URL required.");
        }
        // Discover the external server's OAuth metadata
        OAuthMetadata? meta;
        try
        {
            meta = await _oauth.DiscoverMetadataAsync(conn.EndpointUrl, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to discover OAuth metadata for {Url}", conn.EndpointUrl);
            return BadRequest($"Failed to discover OAuth metadata: {ex.Message}");
        }

        if (meta is null || string.IsNullOrEmpty(meta.AuthorizationEndpoint) || string.IsNullOrEmpty(meta.TokenEndpoint))
        {
            return BadRequest("External server does not expose OAuth authorization/token endpoints.");
        }
        // Dynamic Client Registration if supported
        var clientId = conn.OAuthClientId;
        if (string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(meta.RegistrationEndpoint))
        {
            try
            {
                clientId = await _oauth.RegisterClientAsync(meta.RegistrationEndpoint,
                    Url.Action(nameof(Callback), "McpOAuth", null, Request.Scheme, Request.Host.Value)!, HttpContext.RequestAborted);
                _log.LogInformation("DCR registered client {ClientId} at {Url}", clientId, meta.RegistrationEndpoint);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "DCR failed at {Url}", meta.RegistrationEndpoint);
                return BadRequest($"Dynamic Client Registration failed: {ex.Message}");
            }
        }

        if (string.IsNullOrEmpty(clientId))
        {
            return BadRequest("No client_id available. The external server does not support DCR and no client_id was configured.");
        }
        // Build PKCE
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ToBase64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        // Callback URL — this endpoint
        var callbackUrl = Url.Action(nameof(Callback), "McpOAuth", null, Request.Scheme, Request.Host.Value)!;

        // Bind the callback to authenticated state with a server-enforced expiry.
        var statePayload = JsonSerializer.Serialize(new OAuthState
        {
            ConnectionId = connectionId,
            CodeVerifier = codeVerifier,
            ClientId = clientId,
            TokenEndpoint = meta.TokenEndpoint,
            CallbackUrl = callbackUrl,
            State = state,
        });
        Response.Cookies.Append(StateCookie, _stateProtector.Protect(statePayload, StateCookieLifetime), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = StateCookieLifetime,
            Path = "/",
        });

        // Build the authorization URL
        var sep = meta.AuthorizationEndpoint.Contains('?') ? "&" : "?";
        var authUrl = $"{meta.AuthorizationEndpoint}{sep}"
            + $"response_type=code"
            + $"&client_id={Uri.EscapeDataString(clientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}"
            + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
            + $"&code_challenge_method=S256"
            + $"&state={Uri.EscapeDataString(state)}"
            + (string.IsNullOrEmpty(conn.OAuthScopes) ? "" : $"&scope={Uri.EscapeDataString(conn.OAuthScopes)}");

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
        {
            return ReturnToOpener($"OAuth error: {error}", false);
        }
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return ReturnToOpener("Missing authorization code or state.", false);
        }
        // Read state from cookie
        if (!Request.Cookies.TryGetValue(StateCookie, out var stateJson) || string.IsNullOrEmpty(stateJson))
        {
            return ReturnToOpener("OAuth session expired. Please try again.", false);
        }
        OAuthState? oauthState;
        try
        {
            oauthState = JsonSerializer.Deserialize<OAuthState>(_stateProtector.Unprotect(stateJson), Json);
        }
        catch
        {
            return ReturnToOpener("Invalid OAuth session.", false);
        }

        if (oauthState is null)
        {
            return ReturnToOpener("Invalid OAuth session.", false);
        }
        // Verify state matches the one we sent to the provider
        if (string.IsNullOrEmpty(oauthState.State)
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(state),
                Encoding.UTF8.GetBytes(oauthState.State)))
        {
            return ReturnToOpener("State mismatch — possible CSRF attack.", false);
        }

        // Exchange authorization code for tokens
        try
        {
            var name = await _oauth.CompleteAsync(oauthState.ConnectionId, oauthState.TokenEndpoint,
                code, oauthState.CallbackUrl, oauthState.ClientId, oauthState.CodeVerifier, HttpContext.RequestAborted);
            if (name is null)
            {
                return ReturnToOpener("MCP connection not found.", false);
            }
            // Clear the state cookie
            Response.Cookies.Delete(StateCookie);

            return ReturnToOpener($"Connected to {name}", true);
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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh([FromQuery] Guid connectionId)
    {
        if (await _oauth.GetConnectionAsync(connectionId, HttpContext.RequestAborted) is null)
        {
            return NotFound("MCP connection not found.");
        }
        try
        {
            var token = await _oauth.RefreshAsync(connectionId, HttpContext.RequestAborted);
            return Ok(new { Connected = true, token.ExpiresAt });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Token refresh failed for connection {ConnectionId}", connectionId);
            return BadRequest($"Token refresh failed: {ex.Message}");
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────────────

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
<div style='font-family:system-ui;padding:40px;text-align:center;color:{(success ? "#404040" : "#e5534b")}'>
<p>{System.Net.WebUtility.HtmlEncode(message)}</p>
<p>This window should close automatically.</p>
</div>
</body></html>";
        return Content(html, "text/html");
    }

    // ── Internal DTOs ──────────────────────────────────────────────────────────────

    private sealed class OAuthState
    {
        public Guid ConnectionId
        {
            get; set;
        }
        public string CodeVerifier { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string TokenEndpoint { get; set; } = "";
        public string CallbackUrl { get; set; } = "";
        public string? State
        {
            get; set;
        }
    }

}
