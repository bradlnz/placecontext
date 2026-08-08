using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;

namespace PlaceContext.Jobs.Controllers;

/// <summary>
/// Shared-key-gated external event ingress. Emitted events are persisted and fan out through the
/// Jobs service's event-trigger dispatcher.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting("public-ingestion")]
public sealed class EventIngestionController(
    IDispatcher dispatcher,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost("/ingest/{eventName}")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Ingest(
        string eventName,
        [FromQuery] Guid? projectId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(eventName) || eventName.Length > 200)
            return BadRequest(new { error = "Event name is required and must not exceed 200 characters." });

        var configuredKey = configuration["PlaceContext:Ingest:Key"];
        if (string.IsNullOrWhiteSpace(configuredKey))
            return StatusCode(StatusCodes.Status404NotFound);

        var presentedKey = Request.Headers["X-Ingest-Key"].ToString();
        if (!SecureEquals(presentedKey, configuredKey))
            return Unauthorized();

        string? payload = null;
        if (Request.ContentLength is > 0)
        {
            using var reader = new StreamReader(Request.Body);
            payload = await reader.ReadToEndAsync(ct);
        }

        var result = await dispatcher.Send(
            new EmitEventCommand(eventName, projectId, payload),
            ct);
        return Ok(new { result.Name, result.TriggeredRuns, result.OccurredAt });
    }

    private static bool SecureEquals(string? presented, string expected)
    {
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented ?? string.Empty));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(presentedHash, expectedHash);
    }
}
