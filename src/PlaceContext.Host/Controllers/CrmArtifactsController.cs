using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Host.Controllers;

[Authorize(Policy = Permission.CrmView)]
public sealed class CrmArtifactsController : ControllerBase
{
    private readonly ICrmClientArtifactRepository _artifacts;
    private readonly CrmUserScope _scope;
    private readonly IObjectStore _store;

    public CrmArtifactsController(
        ICrmClientArtifactRepository artifacts,
        CrmUserScope scope,
        IObjectStore store)
        => (_artifacts, _scope, _store) = (artifacts, scope, store);

    [HttpGet("/crm/artifacts/{artifactId:guid}")]
    public async Task<IActionResult> Get(Guid artifactId)
    {
        var artifact = await _artifacts.GetByIdAsync(artifactId, HttpContext.RequestAborted);
        if (artifact is null) return NotFound();
        await _scope.EnsureClientAccessAsync(artifact.ProjectId, artifact.ClientId, HttpContext.RequestAborted);
        var value = await _store.OpenReadAsync(
            artifact.Bucket, artifact.ObjectKey, HttpContext.RequestAborted);
        if (value is null) return NotFound();
        return InlinePreview.StreamResult(
            Response, value.Content, value.ContentType, artifact.Title);
    }
}
