using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Infrastructure.Billing;

namespace PlaceContext.Host.Controllers;

/// <summary>Stripe webhooks are the only automatic authority for paid run entitlements.
/// Never enable plans based on success-return query parameters or arbitrary admin POST bodies.</summary>
[ApiController]
[Route("api/billing/stripe/webhook")]
[AllowAnonymous]
public sealed class StripeRunBillingWebhookController(StripeRunBillingWebhook billing) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        var signature = Request.Headers["Stripe-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(signature)) return Unauthorized();
        using var reader = new StreamReader(Request.Body, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        string body;
        try { body = await reader.ReadToEndAsync(ct); }
        catch (DecoderFallbackException) { return BadRequest(); }
        if (body.Length > 1024 * 1024) return BadRequest();
        try
        {
            await billing.ApplyAsync(body, signature, ct);
            return Ok();
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (System.Text.Json.JsonException) { return BadRequest(); }
        catch (InvalidOperationException) { return BadRequest(); }
        // Transient database failures return 5xx so Stripe retries rather than dropping an event.
    }
}
