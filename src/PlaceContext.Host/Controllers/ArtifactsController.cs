using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Streams a post-job artifact from the object store (MinIO). The portal/TUI link here; the
/// <see cref="IRunArtifactLinkRepository"/> tenant filter scopes the lookup to the signed-in tenant, so
/// one tenant can't read another's artifacts. HTML, images, and PDFs render inline (the browser previews
/// them in the tab); the rest download with their original filename.
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

        var inline = obj.ContentType.StartsWith("text/html") || obj.ContentType.StartsWith("image/")
            || obj.ContentType.StartsWith("application/pdf");
        var fileName = inline ? null : link.ObjectKey[(link.ObjectKey.LastIndexOf('/') + 1)..];
        return File(obj.Content, obj.ContentType, fileDownloadName: fileName);
    }
}
