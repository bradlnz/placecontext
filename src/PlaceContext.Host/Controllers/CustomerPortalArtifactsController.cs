using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Tenant-scoped artifact access for the external customer portal. Artifacts are exposed through
/// their CRM client association so portal users never browse unrelated project or run output.
/// </summary>
[ApiController]
[Route("api/customer-portal")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
public sealed class CustomerPortalArtifactsController : ControllerBase
{
    private readonly IPlaceContextService _service;
    private readonly ICrmClientArtifactRepository _artifacts;
    private readonly IObjectStore _store;

    public CustomerPortalArtifactsController(
        IPlaceContextService service,
        ICrmClientArtifactRepository artifacts,
        IObjectStore store)
        => (_service, _artifacts, _store) = (service, artifacts, store);

    [HttpGet("clients/{clientId:guid}/artifacts")]
    public async Task<ActionResult<IReadOnlyList<CrmClientArtifactView>>> List(
        Guid clientId,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        if (clientId == Guid.Empty) return BadRequest("client_id is required.");
        return Ok(await _service.ListCrmClientArtifactsAsync(clientId, Math.Clamp(take, 1, 500), ct));
    }

    [HttpGet("clients/{clientId:guid}/artifacts/{artifactId:guid}")]
    public async Task<IActionResult> Get(
        Guid clientId,
        Guid artifactId,
        CancellationToken ct = default)
    {
        var artifact = await _artifacts.GetByIdAsync(artifactId, ct);
        if (artifact is null || artifact.ClientId != clientId) return NotFound("Artifact not found.");

        var value = await _store.OpenReadAsync(artifact.Bucket, artifact.ObjectKey, ct);
        if (value is null) return NotFound("Artifact not found.");

        return InlinePreview.StreamResult(
            Response,
            value.Content,
            string.IsNullOrWhiteSpace(value.ContentType) ? artifact.ContentType : value.ContentType,
            artifact.Title);
    }
}
