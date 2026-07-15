using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Infrastructure.Security;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// External event ingress: shared-key-gated webhook endpoint rather than a signed-in operator.
/// Key-check + 404-when-unconfigured shapes are preserved exactly from the minimal-API handlers
/// they replace.
/// </summary>
[AllowAnonymous]
public sealed class IngestController : ControllerBase
{
    private readonly IPlaceContextService _svc;
    private readonly IConfiguration _config;

    public IngestController(IPlaceContextService svc, IConfiguration config)
    {
        _svc = svc;
        _config = config;
    }

    // An external source (a form on a site, a Cloudflare Queue consumer, a webhook) POSTs an event here;
    // it is emitted into this tenant (resolved by subdomain) and fires any subscribed event-triggers, with
    // the JSON body injected as the triggered runs' input payload. Gated by a shared ingest key
    // (PlaceContext:Ingest:Key); disabled when no key is configured to avoid an open relay.
    [HttpPost("/ingest/{eventName}")]
    public async Task<IActionResult> Ingest(string eventName, Guid? projectId)
    {
        var configuredKey = _config["PlaceContext:Ingest:Key"];
        if (string.IsNullOrWhiteSpace(configuredKey))
            return StatusCode(StatusCodes.Status404NotFound);

        var presented = Request.Headers["X-Ingest-Key"].ToString();
        if (!SecureCompare.Equals(presented, configuredKey))
            return Unauthorized();

        string? payload = null;
        if (Request.ContentLength is > 0)
        {
            using var reader = new StreamReader(Request.Body);
            payload = await reader.ReadToEndAsync(HttpContext.RequestAborted);
        }

        var result = await _svc.EmitEventAsync(eventName, projectId, payload, HttpContext.RequestAborted);
        return Ok(new { result.Name, result.TriggeredRuns, result.OccurredAt });
    }
}
