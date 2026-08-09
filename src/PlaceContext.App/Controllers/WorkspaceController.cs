using Microsoft.AspNetCore.Mvc;
using PlaceContext.App.Authentication;

namespace PlaceContext.App.Controllers;

[ApiController]
[Route("api/v1/workspace")]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class WorkspaceController(EdgeCallerContext caller) : ControllerBase
{
    [HttpGet("session")]
    public async Task<ActionResult<SessionResponse>> Session()
    {
        var identity = await caller.AuthenticateAsync(HttpContext);
        return identity is null
            ? Unauthorized()
            : Ok(new SessionResponse(identity.DisplayName, identity.Role, identity.Tenant));
    }
}
