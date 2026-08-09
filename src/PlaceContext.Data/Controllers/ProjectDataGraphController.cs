using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Data.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/data-graph")]
[Authorize(Policy = Permission.DataRead)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ProjectDataGraphController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<GraphVizView>> Get(
        Guid projectId,
        CancellationToken cancellationToken) =>
        Ok(await dispatcher.Query(new GetGraphVizQuery(projectId), cancellationToken));
}
