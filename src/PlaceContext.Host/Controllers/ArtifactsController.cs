using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Streams a post-job artifact from the object store (MinIO). The portal Artifacts page embeds
/// previewable types in a same-origin <c>iframe</c>; the <see cref="IRunArtifactLinkRepository"/>
/// tenant filter scopes the lookup so one tenant cannot read another's artifacts.
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

        var contentType = string.IsNullOrWhiteSpace(obj.ContentType)
            ? "application/octet-stream"
            : obj.ContentType;
        var fileName = link.ObjectKey[(link.ObjectKey.LastIndexOf('/') + 1)..];
        if (string.IsNullOrEmpty(fileName)) fileName = "artifact";

        // Relative iframe src (/runs/…) uses the browser's current host — correct behind DNS/TLS
        // reverse proxies. Do not rewrite to Request.Host (often the internal k8s name without ForwardedHeaders).
        return InlinePreview.StreamResult(Response, obj.Content, contentType, fileName);
    }
}
