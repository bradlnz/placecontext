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

        var isHtml = IsHtmlOrSvg(contentType);
        var inline = CanPreviewInline(contentType);

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Portal embeds previews in an iframe on the same host (including public DNS names).
        // Override the global baseline if anything else set DENY, and allow same-origin framing.
        Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        // Explicit CSP frame-ancestors for modern browsers (pairs with XFO above).
        Response.Headers["Content-Security-Policy"] = isHtml
            // HTML/SVG from jobs is untrusted — sandbox strips script capability even if framed.
            ? "sandbox; frame-ancestors 'self'"
            : "frame-ancestors 'self'";

        // Relative iframe src (/runs/…) uses the browser's current host — correct behind DNS/TLS
        // reverse proxies. Do not rewrite to Request.Host (often the internal k8s name without ForwardedHeaders).
        if (inline)
        {
            // Content-Disposition: inline so the iframe renders; filename still available for "Save as".
            Response.Headers["Content-Disposition"] =
                $"inline; filename=\"{fileName.Replace("\"", "")}\"";
            // Keep real content type for images/PDF/text so the browser can render.
            // HTML is served as text/html under CSP sandbox (not as the portal document).
            return File(obj.Content, isHtml ? "text/html; charset=utf-8" : contentType);
        }

        return File(obj.Content, contentType, fileDownloadName: fileName);
    }

    private static bool IsHtmlOrSvg(string contentType) =>
        contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
        || contentType.Contains("svg", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Types the Artifacts viewer may show in an iframe (must stay in sync with
    /// <c>Artifacts.razor</c> <c>Previewable</c>).
    /// </summary>
    private static bool CanPreviewInline(string contentType)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return true;
        if (contentType.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase)) return true;
        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return true;
        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return true;
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase)) return true;
        if (contentType.Contains("svg", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
