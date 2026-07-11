using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using PlaceContext.Application.Ports;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace PlaceContext.Host.Auth;

/// <summary>
/// A minimal first-party OAuth 2.1 authorization server (authorization-code + PKCE, dynamic client
/// registration) that issues tenant-scoped JWT access tokens for the MCP resource. It reuses the
/// portal's cookie login as the user-authentication + consent step, so an MCP client (e.g. Claude Code)
/// authenticates as a real PlaceContext user, bound to that user's tenant. Endpoints are host-relative,
/// so each tenant subdomain is its own issuer.
/// </summary>
public static class OAuthServer
{
    public static void MapOAuthServer(this WebApplication app)
    {
        // Protected Resource Metadata (RFC 9728) — points clients at this host's authorization server.
        app.MapGet("/.well-known/oauth-protected-resource", (HttpContext ctx) =>
        {
            var b = BaseUrl(ctx);
            return Results.Json(new Dictionary<string, object?>
            {
                ["resource"] = $"{b}/mcp",
                ["authorization_servers"] = new[] { b },
                ["scopes_supported"] = new[] { "mcp" },
                ["bearer_methods_supported"] = new[] { "header" },
            });
        }).AllowAnonymous();

        // Authorization Server Metadata (RFC 8414).
        app.MapGet("/.well-known/oauth-authorization-server", (HttpContext ctx) =>
        {
            var b = BaseUrl(ctx);
            return Results.Json(new Dictionary<string, object?>
            {
                ["issuer"] = b,
                ["authorization_endpoint"] = $"{b}/connect/authorize",
                ["token_endpoint"] = $"{b}/connect/token",
                ["registration_endpoint"] = $"{b}/connect/register",
                ["jwks_uri"] = $"{b}/.well-known/jwks.json",
                ["response_types_supported"] = new[] { "code" },
                ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
                ["code_challenge_methods_supported"] = new[] { "S256" },
                ["token_endpoint_auth_methods_supported"] = new[] { "none" },
                ["scopes_supported"] = new[] { "mcp" },
            });
        }).AllowAnonymous();

        app.MapGet("/.well-known/jwks.json", () => Results.Json(OAuthKeys.Jwks())).AllowAnonymous();

        // Dynamic Client Registration (RFC 7591) — open registration for public PKCE clients (persisted).
        app.MapPost("/connect/register", async (HttpContext ctx, IOAuthClientStore clients) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<RegisterRequest>();
            var uris = req?.RedirectUris ?? [];
            if (uris.Length == 0)
                return Results.BadRequest(new { error = "invalid_redirect_uri" });

            var client = await clients.RegisterAsync(uris, req?.ClientName ?? "MCP Client", ctx.RequestAborted);
            return Results.Json(new Dictionary<string, object?>
            {
                ["client_id"] = client.ClientId,
                ["redirect_uris"] = client.RedirectUris,
                ["token_endpoint_auth_method"] = "none",
                ["grant_types"] = new[] { "authorization_code", "refresh_token" },
                ["response_types"] = new[] { "code" },
            }, statusCode: 201);
        }).AllowAnonymous();

        // Authorize — requires a logged-in user (cookie). Auto-consents and issues a code.
        app.MapGet("/connect/authorize", async (HttpContext ctx, OAuthStore store, IOAuthClientStore clients, ICurrentTenant tenant,
            string client_id, string redirect_uri, string response_type,
            string code_challenge, string? code_challenge_method, string? scope, string? state) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
                // Send the browser to the portal login, then back to this authorize URL so the OAuth
                // round-trip completes. A relative path keeps it a safe local redirect after login.
                return Results.Challenge(
                    new AuthenticationProperties { RedirectUri = ctx.Request.Path + ctx.Request.QueryString },
                    [CookieAuthenticationDefaults.AuthenticationScheme]);

            if (string.IsNullOrWhiteSpace(client_id) || string.IsNullOrWhiteSpace(redirect_uri))
                return Results.BadRequest("Missing client_id or redirect_uri.");
            // Self-heal: register the client (or add this redirect URI) if it isn't on record — handles a
            // public PKCE client whose DCR registration was lost (e.g. after a database reset).
            var client = await clients.EnsureAsync(client_id, redirect_uri, ctx.RequestAborted);
            if (!client.RedirectUris.Contains(redirect_uri))
                return Results.BadRequest("Unknown client or redirect_uri.");
            if (response_type != "code" || string.IsNullOrEmpty(code_challenge) || (code_challenge_method ?? "S256") != "S256")
                return Results.BadRequest("Only response_type=code with S256 PKCE is supported.");

            var userId = Guid.Parse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = ctx.User.FindFirstValue(ClaimTypes.Role) ?? "Viewer";
            var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            store.SaveCode(new AuthCode(code, client_id, redirect_uri, code_challenge, userId, tenant.TenantId,
                role, scope ?? "mcp", DateTimeOffset.UtcNow.AddMinutes(5)));

            var sep = redirect_uri.Contains('?') ? "&" : "?";
            var loc = $"{redirect_uri}{sep}code={Uri.EscapeDataString(code)}";
            if (!string.IsNullOrEmpty(state)) loc += $"&state={Uri.EscapeDataString(state)}";
            return Results.Redirect(loc);
        }).AllowAnonymous();

        // Token — authorization_code (code + PKCE verifier) or refresh_token (auto-renew). Both return
        // a tenant-scoped JWT access token plus a rotated refresh token, so MCP clients renew for as
        // long as they stay active — no hourly browser round-trip.
        app.MapPost("/connect/token", async (HttpContext ctx, OAuthStore store, IOAuthRefreshTokenStore refreshTokens) =>
        {
            var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
            string F(string key) => form.TryGetValue(key, out var v) ? v.ToString() : "";

            switch (F("grant_type"))
            {
                case "authorization_code":
                {
                    var ac = store.TakeCode(F("code"), DateTimeOffset.UtcNow);
                    if (ac is null || ac.ClientId != F("client_id") || ac.RedirectUri != F("redirect_uri"))
                        return Results.BadRequest(new { error = "invalid_grant" });
                    if (!VerifyPkce(F("code_verifier"), ac.CodeChallenge))
                        return Results.BadRequest(new { error = "invalid_grant", error_description = "PKCE verification failed" });

                    var grant = await refreshTokens.IssueAsync(ac.ClientId, ac.UserId, ac.TenantId, ac.Role, ac.Scope, ctx.RequestAborted);
                    return TokenResponse(BaseUrl(ctx), grant);
                }
                case "refresh_token":
                {
                    var grant = await refreshTokens.RotateAsync(F("refresh_token"), F("client_id"), ctx.RequestAborted);
                    if (grant is null)
                        return Results.BadRequest(new { error = "invalid_grant", error_description = "Refresh token is expired, revoked, or already used." });
                    return TokenResponse(BaseUrl(ctx), grant);
                }
                default:
                    return Results.BadRequest(new { error = "unsupported_grant_type" });
            }
        }).AllowAnonymous().DisableAntiforgery();
    }

    private static string BaseUrl(HttpContext ctx) => $"{ctx.Request.Scheme}://{ctx.Request.Host}";

    /// <summary>
    /// MCP access tokens are effectively non-expiring: a connected agent must never lose access
    /// because a session crossed an hour boundary, and not every MCP client refreshes reliably.
    /// This is safe here because expiry is not the revocation mechanism — the bearer pipeline
    /// re-checks the user against the DB on every request (ghost tokens are rejected, roles
    /// refreshed), so removing a member kills their tokens immediately regardless of lifetime.
    /// </summary>
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromDays(3650);

    private static IResult TokenResponse(string issuer, OAuthRefreshGrant grant) =>
        Results.Json(new Dictionary<string, object?>
        {
            ["access_token"] = IssueToken(issuer, grant),
            ["token_type"] = "Bearer",
            ["expires_in"] = (long)AccessTokenLifetime.TotalSeconds,
            ["refresh_token"] = grant.Token,
            ["scope"] = grant.Scope,
        });

    private static string IssueToken(string issuer, OAuthRefreshGrant grant)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = $"{issuer}/mcp",
            Expires = DateTime.UtcNow.Add(AccessTokenLifetime),
            SigningCredentials = OAuthKeys.SigningCredentials,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = grant.UserId.ToString(),
                ["tenant"] = grant.TenantId.ToString(),
                ["role"] = grant.Role,
                ["scope"] = grant.Scope,
            },
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static bool VerifyPkce(string verifier, string challenge)
    {
        if (string.IsNullOrEmpty(verifier)) return false;
        var computed = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
    }

    private sealed record RegisterRequest(
        [property: JsonPropertyName("redirect_uris")] string[]? RedirectUris,
        [property: JsonPropertyName("client_name")] string? ClientName);
}
