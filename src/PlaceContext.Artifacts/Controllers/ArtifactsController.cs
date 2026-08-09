using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Artifacts.Contracts.Api;

namespace PlaceContext.Artifacts.Controllers;

[ApiController]
[Route("api/artifacts")]
[Authorize(Policy = Permission.ArtifactsView)]
[Produces("application/json")]
public sealed class ArtifactsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("recent")]
    public async Task<IActionResult> ListRecent([FromQuery] int take = 100, CancellationToken ct = default)
        => Ok(await dispatcher.Query(new ListRecentArtifactsQuery(Math.Clamp(take, 1, 1000)), ct));

    [HttpGet("projects/{projectId:guid}")]
    public async Task<IActionResult> ListProject(
        Guid projectId, [FromQuery] int take = 2000, [FromQuery] string? search = null,
        CancellationToken ct = default)
        => Ok(await dispatcher.Query(
            new ListProjectArtifactsQuery(projectId, Math.Clamp(take, 1, 5000), search), ct));

    [HttpGet("runs/{runId:guid}")]
    public async Task<IActionResult> ListRun(Guid runId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListRunArtifactsQuery(runId), ct));

    [HttpGet("capabilities")]
    public ActionResult<ArtifactCapabilitiesResponse> Capabilities()
        => Ok(new ArtifactCapabilitiesResponse(
            User.HasClaim("permission", Permission.ArtifactsDelete),
            User.HasClaim("permission", Permission.ArtifactsShare),
            User.HasClaim("permission", Permission.SettingsManage)));

    [HttpDelete]
    [Authorize(Policy = Permission.ArtifactsDelete)]
    public async Task<IActionResult> Delete(DeleteArtifactsRequest request, CancellationToken ct)
    {
        if (request.ArtifactIds.Count == 0)
            return BadRequest(new { error = "Select at least one artifact." });

        var deleted = await dispatcher.Send(
            new DeleteArtifactsCommand(request.ArtifactIds.Distinct().ToList()),
            ct);
        return Ok(new { deleted });
    }

    [HttpGet("{artifactId:guid}/share")]
    [Authorize(Policy = Permission.ArtifactsShare)]
    public async Task<IActionResult> ShareStatus(Guid artifactId, CancellationToken ct)
        => Ok(await dispatcher.Query(new GetArtifactShareStatusQuery(artifactId), ct));

    [HttpPost("{artifactId:guid}/share")]
    [Authorize(Policy = Permission.ArtifactsShare)]
    public async Task<IActionResult> CreateShare(
        Guid artifactId,
        CreateArtifactShareRequest request,
        CancellationToken ct)
    {
        if (request.LifetimeDays is not (1 or 7 or 30))
            return BadRequest(new { error = "Share lifetime must be 1, 7, or 30 days." });

        return Ok(await dispatcher.Send(
            new CreateArtifactShareCommand(artifactId, request.LifetimeDays),
            ct));
    }

    [HttpDelete("{artifactId:guid}/share")]
    [Authorize(Policy = Permission.ArtifactsShare)]
    public async Task<IActionResult> RevokeShare(Guid artifactId, CancellationToken ct)
        => await dispatcher.Send(new RevokeArtifactShareCommand(artifactId), ct)
            ? NoContent()
            : NotFound(new { error = "No active share link exists." });
}
