using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Public artifact download surface. Possession of an unexpired share code is the authorization;
/// invalid, expired, revoked, and missing-object cases deliberately share the same 404 response.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("artifact-share")]
public sealed class SharedArtifactsController : ControllerBase
{
    private readonly IArtifactShareTokenService _shares;
    private readonly IObjectStore _store;

    public SharedArtifactsController(IArtifactShareTokenService shares, IObjectStore store)
        => (_shares, _store) = (shares, store);

    [HttpGet("/share/artifacts/{token}")]
    public async Task<IActionResult> Get(string token)
    {
        var artifact = await _shares.ResolveAsync(token, HttpContext.RequestAborted);
        if (artifact is null) return NotFound();

        var obj = await _store.OpenReadAsync(
            artifact.Bucket, artifact.ObjectKey, HttpContext.RequestAborted);
        if (obj is null) return NotFound();

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";

        var contentType = string.IsNullOrWhiteSpace(obj.ContentType)
            ? artifact.ContentType
            : obj.ContentType;
        if (string.IsNullOrWhiteSpace(contentType)) contentType = "application/octet-stream";
        var fileName = artifact.ObjectKey[(artifact.ObjectKey.LastIndexOf('/') + 1)..];
        if (string.IsNullOrWhiteSpace(fileName)) fileName = artifact.Title;
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "artifact";

        return InlinePreview.StreamResult(Response, obj.Content, contentType, fileName);
    }
}
