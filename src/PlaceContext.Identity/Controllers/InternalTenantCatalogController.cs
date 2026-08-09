using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;

namespace PlaceContext.Identity.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = "ApiKey")]
[Route("api/identity/internal/tenants")]
public sealed class InternalTenantCatalogController(ITenantCatalog tenants) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await tenants.ListAsync(ct));

    [HttpGet("{tenantId:guid}")]
    public async Task<IActionResult> Find(Guid tenantId, CancellationToken ct)
        => await tenants.FindAsync(tenantId, ct) is { } tenant ? Ok(tenant) : NotFound();
}
