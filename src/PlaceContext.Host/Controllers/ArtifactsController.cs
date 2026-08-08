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
[Authorize(Policy = Permission.ArtifactsView)]
public sealed class ArtifactsController : ControllerBase
{
    private readonly IRunArtifactLinkRepository _links;
    private readonly IObjectStore _store;

    public ArtifactsController(IRunArtifactLinkRepository links, IObjectStore store)
        => (_links, _store)
        = (links, store);

    [HttpGet("/runs/{runId:guid}/artifacts/{artifactId:guid}")]
    public async Task<IActionResult> Get(Guid runId, Guid artifactId)
    {
        var link = await _links.GetByIdAsync(artifactId, HttpContext.RequestAborted);
        if (link is null || link.RunId != runId)
        {
            return NotFound("Artifact not found");
        }

        var storeObject = await _store.OpenReadAsync(link.Bucket, link.ObjectKey, HttpContext.RequestAborted);
        if (storeObject is null)
        {
            return NotFound("Artifact not found");
        }

        string contentType = storeObject.ContentType;
        if (string.IsNullOrWhiteSpace(storeObject.ContentType) ||
            storeObject.ContentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            contentType = link.ContentType;
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            contentType = "application/octet-stream";
        }

        var fileName = link.ObjectKey[(link.ObjectKey.LastIndexOf('/') + 1)..];
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = "artifact";
        }
        return InlinePreview.StreamResult(Response, storeObject.Content, contentType, fileName);
    }
}
