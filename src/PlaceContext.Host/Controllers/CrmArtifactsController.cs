using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Host.Controllers;

[Authorize(Policy = Permission.DataRead)]
public sealed class CrmArtifactsController : ControllerBase
{
    private readonly ICrmClientArtifactRepository _artifacts;
    private readonly IObjectStore _store;

    public CrmArtifactsController(ICrmClientArtifactRepository artifacts, IObjectStore store)
        => (_artifacts, _store) = (artifacts, store);

    [HttpGet("/crm/artifacts/{artifactId:guid}")]
    public async Task<IActionResult> Get(Guid artifactId)
    {
        var artifact = await _artifacts.GetByIdAsync(artifactId, HttpContext.RequestAborted);
        if (artifact is null) return NotFound();
        var value = await _store.OpenReadAsync(
            artifact.Bucket, artifact.ObjectKey, HttpContext.RequestAborted);
        if (value is null) return NotFound();
        return InlinePreview.StreamResult(
            Response, value.Content, value.ContentType, artifact.Title);
    }
}
