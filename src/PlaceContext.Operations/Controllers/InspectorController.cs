using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Operations.Contracts.Api;

namespace PlaceContext.Operations.Controllers;

[ApiController]
[Route("api/v1/inspector")]
[Authorize(
    AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme,
    Policy = Permission.JobsView)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class InspectorController(IToolCallLog toolCallLog) : ControllerBase
{
    private const int DefaultToolCallLimit = 20;
    private const int MaximumToolCallLimit = 100;

    [HttpGet("tool-calls")]
    public async Task<ActionResult<IReadOnlyList<InspectorToolCallResponse>>> GetToolCalls(
        [FromQuery] int take = DefaultToolCallLimit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var calls = toolCallLog.Recent(Math.Clamp(take, 1, MaximumToolCallLimit));

        return Ok(calls.Select(call => new InspectorToolCallResponse(
            call.Id,
            call.Tool,
            call.Direction,
            call.Project,
            call.Summary,
            call.Status.ToString(),
            call.DurationMs,
            call.RequestJson,
            call.ResponseJson,
            call.At)));
    }
}
