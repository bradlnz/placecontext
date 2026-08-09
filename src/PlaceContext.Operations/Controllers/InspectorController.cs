using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
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
public sealed class InspectorController(IPlaceContextService placeContextService) : ControllerBase
{
    private const int DefaultToolCallLimit = 20;
    private const int MaximumToolCallLimit = 100;

    [HttpGet("tool-calls")]
    public async Task<ActionResult<IReadOnlyList<InspectorToolCallResponse>>> GetToolCalls(
        [FromQuery] int take = DefaultToolCallLimit,
        CancellationToken cancellationToken = default)
    {
        var calls = await placeContextService.GetRecentToolCallsAsync(
            Math.Clamp(take, 1, MaximumToolCallLimit),
            cancellationToken);

        return Ok(calls.Select(call => new InspectorToolCallResponse(
            call.Id,
            call.Tool,
            call.Direction,
            call.Project,
            call.Summary,
            call.Status,
            call.DurationMs,
            call.RequestJson,
            call.ResponseJson,
            call.At)));
    }
}
