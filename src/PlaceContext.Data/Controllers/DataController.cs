using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Data.Controllers;

[ApiController]
[Route("api/data")]
[Authorize(Policy = Permission.DataRead)]
[Produces("application/json")]
public sealed class DataController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/entities")]
    public async Task<IActionResult> ListEntities(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListDataEntitiesQuery(projectId), ct));

    [HttpGet("projects/{projectId:guid}/mappings")]
    public async Task<IActionResult> ListMappings(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListDataMappingsQuery(projectId), ct));

    [HttpGet("projects/{projectId:guid}/tables")]
    public async Task<IActionResult> ListTables(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListProjectDataTablesQuery(projectId), ct));
}
