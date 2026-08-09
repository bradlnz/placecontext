using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;

namespace PlaceContext.Data.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = "ApiKey")]
[Route("api/data/internal/projects/{projectId:guid}/graph-hotspots")]
public sealed class InternalGraphController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid projectId, CancellationToken ct)
    {
        var graph = await dispatcher.Query(new GetGraphVizQuery(projectId), ct);
        return Ok(graph.Nodes.Where(node => node.IsGod).Select(node => new { node.Label, node.Degree }));
    }
}
