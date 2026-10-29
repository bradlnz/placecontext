using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Controllers.Api;

[ApiController]
[AllowAnonymous] // The app credential is limited to its explicitly assigned queues, not a portal user identity.
[Route("api/apps/queues/{queueId:guid}/messages")]
public sealed class AppQueuesController(IAppQueueGateway queues) : ControllerBase
{
    private string Credential => Request.Headers.Authorization.ToString() is { } value && value.StartsWith("Bearer ", StringComparison.Ordinal) ? value[7..] : "";
    [HttpPost]
    [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> Submit(Guid queueId, JsonElement payload, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        try { return Accepted(await queues.SubmitAsync(Credential, queueId, payload, key ?? "", ct)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
    [HttpGet("{trackingId:guid}")]
    public async Task<IActionResult> Get(Guid queueId, Guid trackingId, CancellationToken ct)
    {
        try { var result = await queues.GetAsync(Credential, queueId, trackingId, ct); return result is null ? NotFound() : Ok(result); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }
}
