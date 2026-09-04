using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Security;

namespace PlaceContext.Host.Auth;

/// <summary>
/// Authenticates machine callers (the Terraform provider, and any other IaC/CI client) against a single
/// configured admin key — <c>PlaceContext:Api:Key</c>.
/// Read from <c>Authorization: Bearer &lt;key&gt;</c> or <c>X-Api-Key: &lt;key&gt;</c>.
/// Deliberately separate from the portal cookie and the MCP JWT-bearer
/// scheme (see Program.cs): a caller must opt an endpoint into this scheme explicitly via
/// <c>[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName, ...)]</c>.
///
/// Deny-by-default: with no key configured, every request fails authentication (no open API). The key
/// comparison is constant-time (mirrors <see cref="PlaceContext.Host.Controllers.IngestController"/>'s
/// shared-key checks) so a timing attack can't be used to guess it byte-by-byte, and the presented key is
/// never logged.
///
/// A successful match establishes an admin-equivalent principal (role Owner, so it holds the full
/// permission catalog by default — see <see cref="PlaceContext.Application.Access.RolePermissionDefaults"/>)
/// under a fixed, non-random subject id. That id doesn't correspond to a real member row; the permission
/// override lookup keyed on it simply finds no overrides and falls through to the role default, which is
/// what a single shared admin key should mean. Tenant scoping comes from <c>TenantResolutionMiddleware</c>
/// (subdomain), which runs before authentication and is untouched by this handler — a key is only ever
/// valid against the workspace whose EF query filter it lands inside.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    /// <summary>The authentication scheme name — reference this from <c>[Authorize(AuthenticationSchemes = ...)]</c>.</summary>
    public const string SchemeName = "ApiKey";

    /// <summary>Fixed subject id for every API-key-authenticated request. Reserved (not a real user row) —
    /// chosen away from <see cref="Guid.Empty"/> so it can't collide with an uninitialized/default id
    /// elsewhere in the system.</summary>
    public static readonly Guid PrincipalId = Guid.Parse("00000000-0000-0000-0000-00000000a9a9");

    private readonly IConfiguration _config;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration config)
        : base(options, logger, encoder)
    {
        _config = config;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredKey = _config["PlaceContext:Api:Key"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return Task.FromResult(
                AuthenticateResult.Fail("The API key is not configured: PlaceContext:Api:Key is required."));
        }

        var presented = ExtractKey(Request);
        if (string.IsNullOrEmpty(presented) || !KeysMatch(presented, configuredKey))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var claims = new[]
        {
            new Claim("sub", PrincipalId.ToString()),
            new Claim("role", nameof(UserRole.Owner)),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>Bearer token takes precedence (the documented, RFC-6750-shaped form); falls back to the
    /// X-Api-Key header for callers that can't easily set Authorization.</summary>
    private static string? ExtractKey(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();

        var header = request.Headers["X-Api-Key"].ToString();
        return string.IsNullOrEmpty(header) ? null : header;
    }

    private static bool KeysMatch(string presented, string configured) => SecureCompare.Equals(presented, configured);

    // 401 (not a redirect, not 404) on a missing/failed challenge — matches the REST contract callers expect.
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
