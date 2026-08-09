using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.App.Authentication;
using PlaceContext.App.Dashboard;
using PlaceContext.App.Proxy;

namespace PlaceContext.App.Controllers;

/// <summary>Edge-owned Dashboard composition over Projects, Jobs, Data, and Operations HTTP APIs.</summary>
[ApiController]
[Route("api/v1/dashboard")]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class DashboardController(
    EdgeCallerContext caller,
    IDashboardHttpClient dashboard) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get(
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken)
    {
        var token = await caller.GetServiceTokenAsync(HttpContext);
        if (token is null) return Unauthorized();
        try
        {
            return Ok(await dashboard.GetAsync(projectId, token, cancellationToken));
        }
        catch (EdgeHttpException exception)
        {
            return StatusCode(exception.StatusCode, ErrorPayload(exception.Message));
        }
    }

    [HttpPost("projects/{projectId:guid}/chains/{chainId:guid}/runs")]
    public async Task<ActionResult<RunChainResponse>> RunChain(
        Guid projectId,
        Guid chainId,
        [FromBody] RunChainRequest? request,
        CancellationToken cancellationToken)
    {
        var token = await caller.GetServiceTokenAsync(HttpContext);
        if (token is null) return Unauthorized();
        try
        {
            return Accepted(await dashboard.RunChainAsync(
                projectId, chainId, request, token, cancellationToken));
        }
        catch (EdgeHttpException exception)
        {
            return StatusCode(exception.StatusCode, ErrorPayload(exception.Message));
        }
    }

    private static object ErrorPayload(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new { error = message };
        }
    }
}
