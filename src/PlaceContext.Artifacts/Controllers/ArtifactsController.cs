using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Artifacts.Controllers;

[ApiController]
[Route("api/artifacts")]
[Authorize(Policy = Permission.ArtifactsView)]
[Produces("application/json")]
public sealed class ArtifactsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("recent")]
    public async Task<IActionResult> ListRecent([FromQuery] int take = 100, CancellationToken ct = default)
        => Ok(await dispatcher.Query(new ListRecentArtifactsQuery(Math.Clamp(take, 1, 500)), ct));

    [HttpGet("projects/{projectId:guid}")]
    public async Task<IActionResult> ListProject(
        Guid projectId, [FromQuery] int take = 2000, [FromQuery] string? search = null,
        CancellationToken ct = default)
        => Ok(await dispatcher.Query(
            new ListProjectArtifactsQuery(projectId, Math.Clamp(take, 1, 5000), search), ct));

    [HttpGet("runs/{runId:guid}")]
    public async Task<IActionResult> ListRun(Guid runId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListRunArtifactsQuery(runId), ct));
}
