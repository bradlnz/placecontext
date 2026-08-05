using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/core")]
[Authorize(AuthenticationSchemes = CoreApiAuthenticationHandler.SchemeName)]
[Authorize(Policy = CoreApiScopes.Health)]
[Produces("application/json")]
public sealed class CoreApiHealthController : ControllerBase
{
    private readonly ICurrentTenant _tenant;

    public CoreApiHealthController(ICurrentTenant tenant) => _tenant = tenant;

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            ok = true,
            api = "core",
            tenant = new
            {
                resolved = _tenant.IsResolved,
                id = _tenant.TenantId,
                slug = _tenant.Slug,
            },
            frontend = User.FindFirst("client_id")?.Value,
            issuedAt = DateTimeOffset.UtcNow,
        });
    }

    [HttpGet("whoami")]
    public IActionResult WhoAmI()
    {
        return Ok(new
        {
            clientId = User.FindFirst("client_id")?.Value,
            tenantId = _tenant.TenantId,
            tenantSlug = _tenant.Slug,
            tenantZone = _tenant.TimeZoneId,
        });
    }
}
