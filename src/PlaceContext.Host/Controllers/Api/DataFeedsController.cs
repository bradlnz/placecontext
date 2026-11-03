using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Features;
using PlaceContext.Host.Api;

namespace PlaceContext.Host.Controllers.Api;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/sources/{sourceId:guid}/inbound")]
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(2_000_000)]
public sealed class DataFeedsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    public Task<IActionResult> Receive(Guid projectId, Guid sourceId, ReceiveDataFeedRequest request)
    {
        if (request.Records is null || request.Records.Length > 100 || request.Records.Any(record => record.ValueKind != JsonValueKind.Object || !record.TryGetProperty("id", out _)))
        {
            return Task.FromResult<IActionResult>(BadRequest(new { error = "Send up to 100 objects with an id field." }));
        }
        return Ingest(projectId, sourceId, request.Records.Select(record => new SourceRecord(record.GetProperty("id").ToString(), record.GetRawText())).ToArray(), request.DeletedKeys ?? []);
    }

    [HttpPost("email")]
    public Task<IActionResult> Email(Guid projectId, Guid sourceId, InboundEmailRequest email)
    {
        if (string.IsNullOrWhiteSpace(email.MessageID) || string.IsNullOrWhiteSpace(email.TextBody))
        {
            return Task.FromResult<IActionResult>(BadRequest(new { error = "MessageID and TextBody are required. Forward plain-text email; attachments are not imported." }));
        }
        return Ingest(projectId, sourceId, [new(email.MessageID, JsonSerializer.Serialize(new
        {
            id = email.MessageID, from = email.From, to = email.To, subject = email.Subject, text = email.TextBody, receivedAt = email.Date
        }))], []);
    }

    private async Task<IActionResult> Ingest(Guid projectId, Guid sourceId, SourceRecord[] records, string[] deleted)
    {
        var authorization = Request.Headers.Authorization.ToString();
        var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..] : "";
        if (token.Length is < 43 or > 128)
        {
            return Unauthorized();
        }
        try
        {
            return Ok(await dispatcher.Send(new ReceiveDataFeedCommand(projectId, sourceId, token, records, deleted), HttpContext.RequestAborted));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Invalid feed records or keys. Existing data was retained." });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "Feed unavailable or full; check token, source, limits and embedding availability. Retry conflicts. Existing data was retained." });
        }
    }
}
