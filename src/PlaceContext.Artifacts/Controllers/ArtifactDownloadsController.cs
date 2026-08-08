using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Artifacts.Presentation;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Artifacts.Controllers;

/// <summary>
/// Streams a tenant-scoped run artifact from the Artifacts service's object store.
/// </summary>
[Authorize(Policy = Permission.ArtifactsView)]
public sealed class ArtifactDownloadsController(
    IRunArtifactLinkRepository links,
    IObjectStore store) : ControllerBase
{
    [HttpGet("/runs/{runId:guid}/artifacts/{artifactId:guid}")]
    public async Task<IActionResult> Get(Guid runId, Guid artifactId)
    {
        var link = await links.GetByIdAsync(artifactId, HttpContext.RequestAborted);
        if (link is null || link.RunId != runId)
            return NotFound("Artifact not found");

        var stored = await store.OpenReadAsync(
            link.Bucket,
            link.ObjectKey,
            HttpContext.RequestAborted);
        if (stored is null)
            return NotFound("Artifact not found");

        var contentType = stored.ContentType;
        if (string.IsNullOrWhiteSpace(contentType)
            || contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            contentType = link.ContentType;
        }

        if (string.IsNullOrWhiteSpace(contentType))
            contentType = "application/octet-stream";

        var fileName = link.ObjectKey[(link.ObjectKey.LastIndexOf('/') + 1)..];
        if (string.IsNullOrEmpty(fileName))
            fileName = "artifact";

        return InlineArtifactPreview.StreamResult(
            Response,
            stored.Content,
            contentType,
            fileName);
    }
}
