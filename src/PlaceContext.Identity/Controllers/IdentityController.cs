using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Identity.Contracts.Api;

namespace PlaceContext.Identity.Controllers;

[ApiController]
[Route("api/v1/identity")]
[AllowAnonymous]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class IdentityController(
    IAuthService authService,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("context")]
    public async Task<ActionResult<IdentityContextResponse>> Context(
        CancellationToken cancellationToken)
    {
        var configuredTask = authService.IsUnconfiguredAsync(cancellationToken);
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        var isUnconfigured = await configuredTask;

        if (tokens.RequestToken is null)
            return Problem("The request verification token could not be created.");

        return Ok(new IdentityContextResponse(
            !isUnconfigured,
            tokens.FormFieldName,
            tokens.RequestToken));
    }
}
