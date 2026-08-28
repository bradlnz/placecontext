using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PlaceContext.Application;
using PlaceContext.Infrastructure.Security;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// External event ingress: shared-key-gated webhook endpoint rather than a signed-in operator.
/// Key-check + 404-when-unconfigured shapes are preserved exactly from the minimal-API handlers
/// they replace.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting("public-ingestion")]
public sealed class IngestController : ControllerBase
{
    private readonly PlaceContextService _svc;
    private readonly IConfiguration _config;

    public IngestController(PlaceContextService svc, IConfiguration config)
    {
        _svc = svc;
        _config = config;
    }

    // An external source (a form on a site, a Cloudflare Queue consumer, a webhook) POSTs an event here;
    // it is emitted into this tenant (resolved by subdomain) and fires any subscribed event-triggers, with
    // the JSON body injected as the triggered runs' input payload. Gated by a shared ingest key
    // (PlaceContext:Ingest:Key); disabled when no key is configured to avoid an open relay.
    [HttpPost("/ingest/{eventName}")]
    [HttpPost("/api/ingest/{eventName}")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Ingest(string eventName, Guid? projectId)
    {
        if (string.IsNullOrWhiteSpace(eventName) || eventName.Length > 200)
            return BadRequest(new { error = "Event name is required and must not exceed 200 characters." });
        var configuredKey = _config["PlaceContext:Ingest:Key"];
        if (string.IsNullOrWhiteSpace(configuredKey))
            return StatusCode(StatusCodes.Status404NotFound);

        var presented = PresentedKey(Request);
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

    /// <summary>Accept the purpose-built header plus the two authentication shapes used by the
    /// rest of the public API, so webhook clients are not forced to implement a custom transport.</summary>
    internal static string PresentedKey(HttpRequest request)
    {
        var key = request.Headers["X-Ingest-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key))
            key = request.Headers["X-Api-Key"].ToString();
        if (!string.IsNullOrWhiteSpace(key))
            return key.Trim();

        var authorization = request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        return authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)
            ? authorization[bearer.Length..].Trim()
            : string.Empty;
    }
}
