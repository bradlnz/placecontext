using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Auth;

public sealed class UserApiTokenAuthenticationOptions : AuthenticationSchemeOptions { }

/// <summary>
/// Authenticates personal API tokens minted from Settings (prefix <c>pct_</c>). Accepts
/// <c>Authorization: Bearer pct_…</c> or <c>X-Api-Key: pct_…</c>. Establishes the owning user's
/// principal (role + sub) so fine-grained permission policies apply the same as the portal.
/// Workspace admin keys (<see cref="ApiKeyAuthenticationHandler"/>) remain a separate scheme.
/// </summary>
public sealed class UserApiTokenAuthenticationHandler : AuthenticationHandler<UserApiTokenAuthenticationOptions>
{
    public const string SchemeName = "UserApiToken";

    private readonly IUserApiTokenService _tokens;

    public UserApiTokenAuthenticationHandler(
        IOptionsMonitor<UserApiTokenAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IUserApiTokenService tokens)
        : base(options, logger, encoder)
    {
        _tokens = tokens;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = ExtractKey(Request);
        if (string.IsNullOrEmpty(presented) || !presented.StartsWith("pct_", StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var user = await _tokens.ValidateAsync(presented, Context.RequestAborted);
        if (user is null)
            return AuthenticateResult.Fail("Invalid or expired API token.");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim("sub", user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim("tenant", user.TenantId.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("role", user.Role.ToString()),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    private static string? ExtractKey(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();

        var header = request.Headers["X-Api-Key"].ToString();
        return string.IsNullOrEmpty(header) ? null : header;
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
