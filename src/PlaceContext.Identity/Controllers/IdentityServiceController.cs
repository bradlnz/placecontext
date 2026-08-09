using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Identity.Contracts.Api;

namespace PlaceContext.Identity.Controllers;

[ApiController]
[Route("api/identity")]
public sealed class IdentityServiceController : ControllerBase
{
    [HttpGet("status")]
    [AllowAnonymous]
    public ActionResult<IdentityServiceStatus> Status()
        => Ok(new IdentityServiceStatus("identity", "ready", OAuthEnabled: true));
}
