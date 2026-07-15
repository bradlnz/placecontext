using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Streams a post-job artifact from the object store (MinIO). The portal/TUI link here; the
/// <see cref="IRunArtifactLinkRepository"/> tenant filter scopes the lookup to the signed-in tenant, so
/// one tenant can't read another's artifacts. HTML is always offered as a download (never inline) to
/// prevent stored XSS from malicious job output running in the portal origin; images and PDFs may
/// still render inline.
/// </summary>
[Authorize]
public sealed class ArtifactsController : ControllerBase
{
    private readonly IRunArtifactLinkRepository _links;
    private readonly IObjectStore _store;

    public ArtifactsController(IRunArtifactLinkRepository links, IObjectStore store)
    {
        _links = links;
        _store = store;
    }

    [HttpGet("/runs/{runId:guid}/artifacts/{artifactId:guid}")]
    public async Task<IActionResult> Get(Guid runId, Guid artifactId)
    {
        var link = await _links.GetByIdAsync(artifactId, HttpContext.RequestAborted);
        if (link is null || link.RunId != runId) return NotFound();
        var obj = await _store.OpenReadAsync(link.Bucket, link.ObjectKey, HttpContext.RequestAborted);
        if (obj is null) return NotFound();

        // Never inline HTML/SVG — job authors (or compromised workloads) could inject scripts that
        // run as the signed-in portal user. Force download instead.
        var isHtml = obj.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
            || obj.ContentType.Contains("svg", StringComparison.OrdinalIgnoreCase);
        var inline = !isHtml && (obj.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || obj.ContentType.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase));
        var fileName = link.ObjectKey[(link.ObjectKey.LastIndexOf('/') + 1)..];
        if (string.IsNullOrEmpty(fileName)) fileName = "artifact";

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (isHtml)
            Response.Headers["Content-Security-Policy"] = "sandbox";

        return File(obj.Content, isHtml ? "application/octet-stream" : obj.ContentType,
            fileDownloadName: inline ? null : fileName);
    }
}
