using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PlaceContext.Application.Ports;
using PlaceContext.Artifacts.Presentation;

namespace PlaceContext.Artifacts.Controllers;

/// <summary>
/// Public artifact download surface. Possession of a valid, unexpired share code is the
/// authorization; invalid and missing-object responses are deliberately indistinguishable.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("artifact-share")]
public sealed class SharedArtifactsController(
    IArtifactShareTokenService shares,
    IObjectStore store) : ControllerBase
{
    [HttpGet("/share/artifacts/{token}")]
    public async Task<IActionResult> Get(string token)
    {
        var artifact = await shares.ResolveAsync(token, HttpContext.RequestAborted);
        if (artifact is null) return NotFound();

        var stored = await store.OpenReadAsync(
            artifact.Bucket,
            artifact.ObjectKey,
            HttpContext.RequestAborted);
        if (stored is null) return NotFound();

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";

        var contentType = string.IsNullOrWhiteSpace(stored.ContentType)
            ? artifact.ContentType
            : stored.ContentType;
        if (string.IsNullOrWhiteSpace(contentType))
            contentType = "application/octet-stream";

        var fileName = artifact.ObjectKey[(artifact.ObjectKey.LastIndexOf('/') + 1)..];
        if (string.IsNullOrWhiteSpace(fileName)) fileName = artifact.Title;
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "artifact";

        return InlineArtifactPreview.StreamResult(
            Response,
            stored.Content,
            contentType,
            fileName);
    }
}
