using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;

namespace PlaceContext.ServiceDefaults;

/// <summary>
/// Validates the legacy workspace administration key at a microservice boundary. Endpoints must
/// opt into this scheme explicitly; service-to-service JWT remains the default authentication.
/// </summary>
public sealed class ServiceApiKeyAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IConfiguration _configuration;

    public ServiceApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
        => _configuration = configuration;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configured = _configuration[ServiceApiKeyAuthenticationDefaults.ConfigurationKey];
        if (string.IsNullOrWhiteSpace(configured))
            return Task.FromResult(AuthenticateResult.Fail("The workspace API key is not configured."));

        var presented = ExtractKey(Request);
        if (string.IsNullOrEmpty(presented) || !KeysMatch(presented, configured))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var claims = new List<Claim>
        {
            new("sub", ServiceApiKeyAuthenticationDefaults.Subject),
            new("role", "Owner"),
        };
        claims.AddRange(Permission.All.Select(permission =>
            new Claim(ServiceAuthenticationDefaults.PermissionClaim, permission)));

        var identity = new ClaimsIdentity(claims, ServiceApiKeyAuthenticationDefaults.Scheme);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            ServiceApiKeyAuthenticationDefaults.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    private static string? ExtractKey(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authorization["Bearer ".Length..].Trim();

        var apiKey = request.Headers["X-Api-Key"].ToString();
        return string.IsNullOrEmpty(apiKey) ? null : apiKey;
    }

    private static bool KeysMatch(string presented, string configured)
    {
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        return CryptographicOperations.FixedTimeEquals(presentedHash, configuredHash);
    }
}
