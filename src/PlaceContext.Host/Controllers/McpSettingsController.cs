using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Controllers.Api.Records;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Authorize(
    AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme,
    Policy = Policies.DefaultAdmin)]
[Route("api/v1/settings/mcp")]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class McpSettingsController(IPlaceContextService placeContext) : ControllerBase
{
    [HttpGet("context")]
    public async Task<ActionResult<McpSettingsResponse>> Context(
        [FromQuery] Guid? projectId,
        CancellationToken ct)
    {
        var projects = await placeContext.GetProjectsAsync(ct);
        var selectedProjectId = projectId ?? projects.FirstOrDefault()?.Id;
        if (selectedProjectId is { } selected
            && projects.All(project => project.Id != selected))
            return NotFound(new { error = "Project not found." });

        var connections = selectedProjectId is { } id
            ? await placeContext.ListMcpConnectionsAsync(id, ct)
            : Array.Empty<McpConnectionView>();

        return Ok(new McpSettingsResponse(
            selectedProjectId,
            projects.Select(project => new McpProjectView(project.Id, project.Name)).ToList(),
            connections.Select(ToResponse).ToList()));
    }

    [HttpPost("projects/{projectId:guid}/connections")]
    public async Task<ActionResult<McpConnectionResponse>> Create(
        Guid projectId,
        [FromBody] CreateMcpConnectionRequest request,
        CancellationToken ct)
    {
        try
        {
            var connection = await placeContext.CreateMcpConnectionAsync(
                new CreateMcpConnectionCommand(
                    projectId,
                    request.Name,
                    request.Transport,
                    request.EndpointUrl,
                    request.Command,
                    request.Args,
                    request.AuthType,
                    request.AuthToken,
                    request.AuthHeader,
                    OAuthScopes: request.OAuthScopes),
                ct);
            return Ok(ToResponse(connection));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("connections/{id:guid}/test")]
    public async Task<ActionResult<McpConnectionResponse>> Test(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(ToResponse(await placeContext.TestMcpConnectionAsync(id, ct)));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpDelete("connections/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await placeContext.DeleteMcpConnectionAsync(id, ct)
            ? NoContent()
            : NotFound(new { error = "MCP connection not found." });

    private static McpConnectionResponse ToResponse(McpConnectionView connection)
        => new(
            connection.Id,
            connection.ProjectId,
            connection.Name,
            connection.Transport,
            connection.EndpointUrl,
            connection.Command,
            connection.Args,
            connection.AuthType,
            connection.Enabled,
            connection.LastStatus,
            connection.LastConnectedAt,
            connection.CreatedAt,
            connection.OAuthTokenExpiresAt,
            connection.OAuthClientId,
            connection.OAuthScopes);
}
