using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Access;
using PlaceContext.Application.Jobs;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Streams a post-job artifact from the object store (MinIO). The portal Artifacts page embeds
/// previewable types in a same-origin <c>iframe</c>; the <see cref="ArtifactReader"/>
/// tenant filter scopes the lookup so one tenant cannot read another's artifacts.
/// </summary>
[Authorize(
    AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + AgentAuthenticationDefaults.SchemeName,
    Policy = Permission.ArtifactsView)]
public sealed class ArtifactsController : ControllerBase
{
    private readonly ArtifactReader _artifacts;

    public ArtifactsController(ArtifactReader artifacts) => _artifacts = artifacts;

    [HttpGet("/runs/{runId:guid}/artifacts/{artifactId:guid}")]
    public async Task<IActionResult> Get(Guid runId, Guid artifactId)
    {
        var content = await _artifacts.OpenAsync(artifactId, runId, HttpContext.RequestAborted);
        if (content is null)
        {
            return NotFound();
        }
        return InlinePreview.StreamResult(Response, content.Content, content.ContentType, content.FileName);
    }
}
