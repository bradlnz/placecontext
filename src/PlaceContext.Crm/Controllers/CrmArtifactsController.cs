using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Integration;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Crm.Controllers;

[Authorize(Policy = Permission.CrmView)]
public sealed class CrmArtifactsController : ControllerBase
{
    private readonly ICrmClientArtifactRepository _artifacts;
    private readonly ICrmArtifactsClient _storage;

    public CrmArtifactsController(ICrmClientArtifactRepository artifacts, ICrmArtifactsClient storage)
        => (_artifacts, _storage) = (artifacts, storage);

    [HttpGet("/crm/artifacts/{artifactId:guid}")]
    public async Task<IActionResult> Get(Guid artifactId)
    {
        var artifact = await _artifacts.GetByIdAsync(artifactId, HttpContext.RequestAborted);
        if (artifact is null) return NotFound();
        var value = await _storage.ReadAsync(
            artifact.Bucket, artifact.ObjectKey, HttpContext.RequestAborted);
        if (value is null) return NotFound();
        Response.Headers.ContentDisposition =
            $"inline; filename=\"{artifact.Title.Replace("\"", string.Empty)}\"";
        return File(value.Content, value.ContentType, enableRangeProcessing: true);
    }
}
