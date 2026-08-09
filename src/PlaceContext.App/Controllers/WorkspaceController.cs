using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.App.Authentication;
using PlaceContext.App.Proxy;
using PlaceContext.App.Workspace;

namespace PlaceContext.App.Controllers;

[ApiController]
[Route("api/v1/workspace")]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class WorkspaceController(
    EdgeCallerContext caller,
    IWorkspaceHttpClient? workspace = null) : ControllerBase
{
    [HttpGet("session")]
    public async Task<ActionResult<SessionResponse>> Session()
    {
        var identity = await caller.AuthenticateAsync(HttpContext);
        return identity is null
            ? Unauthorized()
            : Ok(new SessionResponse(identity.DisplayName, identity.Role, identity.Tenant));
    }

    [HttpGet("projects")]
    public Task<ActionResult<JsonElement>> Projects(CancellationToken cancellationToken)
        => ReadAsync((client, token) => client.GetProjectsAsync(token, cancellationToken));

    [HttpGet("focus")]
    public Task<ActionResult<JsonElement>> Focus(CancellationToken cancellationToken)
        => ReadAsync((client, token) => client.GetFocusAsync(token, cancellationToken));

    [HttpGet("stats")]
    public Task<ActionResult<JsonElement>> Stats(CancellationToken cancellationToken)
        => ReadAsync((client, token) => client.GetStatsAsync(token, cancellationToken));

    private async Task<ActionResult<JsonElement>> ReadAsync(
        Func<IWorkspaceHttpClient, string, Task<JsonElement>> read)
    {
        var token = await caller.GetServiceTokenAsync(HttpContext);
        if (token is null) return Unauthorized();
        if (workspace is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        try
        {
            return Ok(await read(workspace, token));
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
