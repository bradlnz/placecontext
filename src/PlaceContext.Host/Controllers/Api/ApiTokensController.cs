using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Controllers.Api.Records;

namespace PlaceContext.Host.Controllers.Api;

[ApiController]
[Route("api/v1/settings/api-tokens")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ApiTokensController(IUserApiTokenService tokens) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserApiTokenView>>> List(CancellationToken cancellationToken)
        => Ok(await tokens.ListMineAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CreatedUserApiToken>> Create(
        [FromBody] CreateApiTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Give the token a name." });
        if (request.LifetimeDays is < 1 or > 365)
            return BadRequest(new { error = "Token lifetime must be between 1 and 365 days." });
        return Ok(await tokens.CreateAsync(request.Name.Trim(), TimeSpan.FromDays(request.LifetimeDays), cancellationToken));
    }

    [HttpDelete("{tokenId:guid}")]
    public async Task<ActionResult<ApiTokenRevocationResponse>> Revoke(Guid tokenId, CancellationToken cancellationToken)
    {
        var revoked = await tokens.RevokeAsync(tokenId, cancellationToken);
        return revoked ? Ok(new ApiTokenRevocationResponse(true)) : NotFound(new { error = "Token not found." });
    }
}
