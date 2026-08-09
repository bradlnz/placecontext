using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;

namespace PlaceContext.Identity.Controllers;

/// <summary>Internal tenant lookup used by authenticated microservice ingress adapters.</summary>
[ApiController]
[Route("api/identity/internal/tenants")]
[Authorize]
public sealed class TenantResolutionController(IRequestTenantResolver tenantResolver) : ControllerBase
{
    [HttpGet("resolve")]
    public async Task<ActionResult<TenantContext>> Resolve(
        [FromQuery] string host,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
            return BadRequest(new { error = "host is required" });

        var tenant = await tenantResolver.ResolveAsync(host, cancellationToken);
        return tenant is null ? NotFound() : Ok(tenant);
    }
}
