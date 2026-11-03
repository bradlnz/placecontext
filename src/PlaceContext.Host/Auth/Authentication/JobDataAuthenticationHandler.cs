using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Jobs;

namespace PlaceContext.Host.Auth;

public sealed class JobDataAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IJobDataTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "JobData";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }
        var grant = await tokens.ValidateAsync(authorization["Bearer ".Length..].Trim(), Context.RequestAborted);
        if (grant is null)
        {
            return AuthenticateResult.Fail("Invalid or inactive job credential.");
        }
        Context.Features.Set(grant);
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, grant.RunId.ToString()),
            new Claim("tenant", grant.TenantId.ToString())
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
