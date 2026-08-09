using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// OCR daemon contract (server side of the plan in docs/OCR-DAEMON-PLAN.md).
/// <list type="bullet">
///   <item><c>GET /api/ocr/pending?take=10</c> — oldest artifacts still awaiting OCR, with their
///   download URLs (<c>/runs/…/artifacts/…</c>, same host/token the daemon already uses).</item>
///   <item><c>POST /api/ocr/complete</c> — body <c>{ artifactId, markdown, error? }</c>; on success
///   the markdown is stored in the project's <c>ocr_results</c> table, and the artifact is marked
///   processed either way.</item>
/// </list>
/// Auth: personal user API tokens (<c>pct_</c>, Bearer or X-Api-Key) or the workspace admin key.
/// Reading the queue needs <c>artifacts.view</c>; completing one needs <c>data.write</c>.
/// </summary>
[ApiController]
[Route("api/ocr")]
[Authorize(AuthenticationSchemes =
    UserApiTokenAuthenticationHandler.SchemeName + "," + ApiKeyAuthenticationHandler.SchemeName)]
[Produces("application/json")]
public sealed class OcrController : ControllerBase
{
    private readonly IPlaceContextService _svc;
    public OcrController(IPlaceContextService svc) => _svc = svc;

    /// <summary>GET /api/ocr/pending — the oldest artifacts still needing OCR (default batch 10).</summary>
    [HttpGet("pending")]
    [Authorize(Policy = Permission.ArtifactsView)]
    public async Task<ActionResult<IReadOnlyList<PendingOcrArtifactView>>> ListPending([FromQuery] int take = 10)
    {
        take = Math.Clamp(take, 1, 100);
        var pending = await _svc.ListPendingOcrAsync(take, HttpContext.RequestAborted);
        return Ok(pending);
    }

    /// <summary>POST /api/ocr/complete — stores extracted markdown (or a failure reason) for one artifact.</summary>
    [HttpPost("complete")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> Complete([FromBody] CompleteOcrRequest request)
    {
        if (request.ArtifactId == Guid.Empty)
            return BadRequest(new { error = "artifactId is required." });
        if (string.IsNullOrWhiteSpace(request.Markdown) && string.IsNullOrWhiteSpace(request.Error))
            return BadRequest(new { error = "Provide markdown (success) or error (failure)." });

        var ok = await _svc.CompleteOcrAsync(request.ArtifactId, request.Markdown, request.Error, HttpContext.RequestAborted);
        if (!ok) return NotFound(new { error = "Artifact not found." });
        return Ok(new { processed = true });
    }
}
