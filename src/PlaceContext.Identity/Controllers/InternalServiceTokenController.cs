using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Identity.Auth;
using PlaceContext.Identity.Contracts.Api;

namespace PlaceContext.Identity.Controllers;

[ApiController]
[Route("api/identity/internal/service-token")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public sealed class InternalServiceTokenController(ServiceTokenIssuer issuer) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ServiceTokenResponse>> Issue(CancellationToken ct)
    {
        var (token, expiresAt) = await issuer.IssueAsync(User, ct);
        return Ok(new ServiceTokenResponse(token, expiresAt));
    }
}
