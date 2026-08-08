using System.Security.Claims;
using System.Linq;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Security;

namespace PlaceContext.Host.Auth;

/// <summary>
/// Machine-to-API auth for the frontend-only Core API surface.
/// </summary>
public sealed class CoreApiAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "CoreApi";

    private readonly CoreApiOptions _coreApiOptions;

    public CoreApiAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<CoreApiOptions> coreApiOptions)
        : base(options, logger, encoder)
    {
        _coreApiOptions = coreApiOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (_coreApiOptions.Clients.Count == 0)
            return Task.FromResult(AuthenticateResult.Fail("Core API is disabled: PlaceContext:CoreApi:Clients is empty."));

        var clientId = Request.Headers[_coreApiOptions.ClientIdHeader].ToString();
        var presented = ExtractKey(Request);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(presented))
            return Task.FromResult(AuthenticateResult.Fail("Missing Core API frontend credentials."));

        var client = _coreApiOptions.Clients.FirstOrDefault(
            c => !string.IsNullOrWhiteSpace(c.Id) &&
                 string.Equals(c.Id, clientId, StringComparison.Ordinal));
        if (client is null || string.IsNullOrWhiteSpace(client.Secret))
            return Task.FromResult(AuthenticateResult.Fail("Unknown Core API frontend client."));

        if (!SecureCompare.Equals(presented, client.Secret))
            return Task.FromResult(AuthenticateResult.Fail("Invalid Core API credentials."));

        if (client.AllowedOrigins.Count > 0)
        {
            var origin = Request.Headers.Origin.ToString();
            if (!string.IsNullOrWhiteSpace(origin) &&
                !client.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            {
                return Task.FromResult(
                    AuthenticateResult.Fail("Origin is not allowed for this Core API frontend client."));
            }
        }

        var scopes = GetClientScopes(client);
        if (scopes is null)
            return Task.FromResult(AuthenticateResult.Fail("Core API client has one or more unknown scopes configured."));

        var claims = new List<Claim>
        {
            new Claim("client_id", clientId),
            new Claim("sub", $"frontend:{clientId}"),
            new Claim(ClaimTypes.Role, nameof(UserRole.Owner)),
            new Claim("role", nameof(UserRole.Owner)),
        };
        claims.AddRange(scopes.Select(s => new Claim("scope", s)));
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private IReadOnlyCollection<string>? GetClientScopes(CoreApiFrontendClient client)
    {
        var configured = (client.AllowedScopes.Count == 0 ? CoreApiScopes.All : client.AllowedScopes)
            .Select(s => s?.Trim() ?? string.Empty)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var unknown = configured
            .Where(s => !CoreApiScopes.IsKnown(s))
            .ToList();
        return unknown.Count == 0 ? configured : null;
    }

    private string? ExtractKey(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();

        var header = request.Headers[_coreApiOptions.ApiKeyHeader].ToString();
        return string.IsNullOrWhiteSpace(header) ? null : header;
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
