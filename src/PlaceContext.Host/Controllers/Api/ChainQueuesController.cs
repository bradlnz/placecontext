using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Access;
using PlaceContext.Application.Jobs;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers.Api;

[ApiController]
[Route("api/v1/queues/{queueId:guid}/messages")]
[Authorize(AuthenticationSchemes = UserApiTokenAuthenticationHandler.SchemeName)]
[Produces("application/json")]
public sealed class ChainQueuesController(IChainQueueService queues) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = Permission.JobsRun)]
    [Consumes("application/json")]
    [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> Submit(Guid queueId, [FromBody] JsonElement payload,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromHeader(Name = "X-Execute-At")] DateTimeOffset? executeAt, CancellationToken ct)
    {
        try
        {
            var receipt = await queues.SubmitAsync(queueId, payload, idempotencyKey, executeAt, ct);
            return AcceptedAtAction(nameof(Get), new
            {
                queueId,
                trackingId = receipt.TrackingId
            }, receipt);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                error = ex.Message
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                error = ex.Message
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{trackingId:guid}")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<IActionResult> Get(Guid queueId, Guid trackingId, CancellationToken ct)
    {
        var receipt = await queues.GetMessageAsync(queueId, trackingId, ct);
        return receipt is null ? NotFound() : Ok(receipt);
    }
}
