using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Features;
using PlaceContext.Host.Demo;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// External event ingress: shared-key-gated endpoints hit by webhooks, demo seeding, and inbound SMS
/// providers rather than by a signed-in operator. All three key-check + 404-when-unconfigured shapes
/// are preserved exactly from the minimal-API handlers they replace.
/// </summary>
[AllowAnonymous]
public sealed class IngestController : ControllerBase
{
    private readonly IPlaceContextService _svc;
    private readonly IConfiguration _config;
    private readonly BrisbaneDemoSeeder _seeder;

    public IngestController(IPlaceContextService svc, IConfiguration config, BrisbaneDemoSeeder seeder)
    {
        _svc = svc;
        _config = config;
        _seeder = seeder;
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
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(presented),
                Encoding.UTF8.GetBytes(configuredKey)))
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

    // Seed the Brisbane property feasibility demo into the resolved tenant: project + Data tables +
    // decisions + context, with the analytics sweep queued. Same gate as /ingest
    // (shared key, disabled when unconfigured); the Onboarding page has the same action as a button.
    [HttpPost("/seed/brisbane-demo")]
    public async Task<IActionResult> SeedBrisbaneDemo()
    {
        var configuredKey = _config["PlaceContext:Ingest:Key"];
        if (string.IsNullOrWhiteSpace(configuredKey))
            return StatusCode(StatusCodes.Status404NotFound);
        var presented = Request.Headers["X-Ingest-Key"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(presented),
                Encoding.UTF8.GetBytes(configuredKey)))
            return Unauthorized();

        var tenant = CurrentTenant.Current;
        if (tenant is null) return BadRequest(new { error = "no tenant resolved" });

        var (projectId, already) = await _seeder.SeedAsync(tenant, HttpContext.RequestAborted);
        return Ok(new { projectId, alreadySeeded = already, project = $"/project/{projectId}" });
    }

    // A delivery provider (Twilio-style form post) or any bridge (JSON) POSTs inbound texts here; the
    // tenant comes from the subdomain (same middleware as /ingest). The sender and body are encrypted
    // before storage and a sms.received event fires (metadata only) for triggers. Gated by a shared
    // key (PlaceContext:Sms:InboundKey) passed as ?key= or X-Sms-Key; disabled when unconfigured.
    [HttpPost("/sms/inbound")]
    public async Task<IActionResult> SmsInbound(Guid? projectId)
    {
        var configuredKey = _config["PlaceContext:Sms:InboundKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
            return StatusCode(StatusCodes.Status404NotFound);

        var presented = Request.Headers["X-Sms-Key"].ToString();
        if (string.IsNullOrEmpty(presented)) presented = Request.Query["key"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(presented),
                Encoding.UTF8.GetBytes(configuredKey)))
            return Unauthorized();

        string from, to, body, provider;
        string? externalId;
        if (Request.HasFormContentType)
        {
            // Twilio-compatible form fields.
            var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
            from = form["From"].ToString();
            to = form["To"].ToString();
            body = form["Body"].ToString();
            externalId = form["MessageSid"].ToString() is { Length: > 0 } sid ? sid : null;
            provider = "twilio";
        }
        else
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body, cancellationToken: HttpContext.RequestAborted);
            var root = doc.RootElement;
            string Get(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
            from = Get("from"); to = Get("to"); body = Get("body");
            externalId = Get("externalId") is { Length: > 0 } eid ? eid : null;
            provider = Get("provider") is { Length: > 0 } p ? p : "generic";
        }

        try
        {
            await _svc.ReceiveInboundSmsAsync(
                new ReceiveInboundSmsCommand(from, to, body, provider, externalId, projectId),
                HttpContext.RequestAborted);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        // Twilio expects TwiML back on form posts; an empty <Response/> means "received, no reply".
        return Request.HasFormContentType
            ? Content("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response></Response>", "text/xml")
            : Ok(new { received = true });
    }
}
