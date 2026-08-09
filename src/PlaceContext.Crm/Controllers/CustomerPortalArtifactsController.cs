using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Crm.Integration;
using PlaceContext.Crm.Presentation;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Crm.Controllers;

[ApiController]
[Route("api/customer-portal")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public sealed class CustomerPortalArtifactsController(
    IDispatcher dispatcher,
    ICrmClientArtifactRepository artifacts,
    ICrmArtifactsClient storage) : ControllerBase
{
    [HttpGet("clients/{clientId:guid}/artifacts")]
    public async Task<IActionResult> List(
        Guid clientId,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        if (clientId == Guid.Empty) return BadRequest("client_id is required.");
        return Ok(await dispatcher.Query(
            new ListCrmClientArtifactsQuery(clientId, Math.Clamp(take, 1, 500)),
            ct));
    }

    [HttpGet("clients/{clientId:guid}/artifacts/{artifactId:guid}")]
    public async Task<IActionResult> Get(
        Guid clientId,
        Guid artifactId,
        CancellationToken ct = default)
    {
        var artifact = await artifacts.GetByIdAsync(artifactId, ct);
        if (artifact is null || artifact.ClientId != clientId)
            return NotFound("Artifact not found.");

        var value = await storage.ReadAsync(artifact.Bucket, artifact.ObjectKey, ct);
        if (value is null) return NotFound("Artifact not found.");

        return InlineCrmArtifactPreview.StreamResult(
            Response,
            new MemoryStream(value.Content, writable: false),
            string.IsNullOrWhiteSpace(value.ContentType) ? artifact.ContentType : value.ContentType,
            artifact.Title);
    }
}
