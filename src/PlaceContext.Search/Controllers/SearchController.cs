using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Search.Controllers;

[ApiController]
[Route("api/search")]
[Authorize(Policy = Permission.DataRead)]
[Produces("application/json")]
public sealed class SearchController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string term, [FromQuery] Guid? projectId = null,
        [FromQuery] int limit = 25, CancellationToken ct = default)
        => Ok(await dispatcher.Query(
            new SearchQuery(term, Math.Clamp(limit, 1, 100), projectId), ct));

    [HttpGet("projects/{projectId:guid}/run-outputs")]
    public async Task<IActionResult> SearchRunOutputs(
        Guid projectId, [FromQuery] string term, [FromQuery] int take = 10,
        CancellationToken ct = default)
        => Ok(await dispatcher.Query(
            new SearchRunOutputsQuery(projectId, term, Math.Clamp(take, 1, 100)), ct));

    [HttpGet("projects/{projectId:guid}/opensearch/indices")]
    public async Task<IActionResult> ListOpenSearchIndices(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListOpenSearchIndicesQuery(projectId), ct));
}
