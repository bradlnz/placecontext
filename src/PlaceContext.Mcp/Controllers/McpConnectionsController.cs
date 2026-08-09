using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Mcp.Contracts.Api;

namespace PlaceContext.Mcp.Controllers;

[ApiController]
[Authorize(Policy = Permission.SettingsManage)]
[Route("api/mcp")]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class McpConnectionsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/connections")]
    public async Task<ActionResult<IReadOnlyList<McpConnectionResponse>>> List(
        Guid projectId,
        CancellationToken ct)
        => Ok((await dispatcher.Query(new ListMcpConnectionsQuery(projectId), ct))
            .Select(ToResponse)
            .ToList());

    [HttpPost("projects/{projectId:guid}/connections")]
    public async Task<ActionResult<McpConnectionResponse>> Create(
        Guid projectId,
        [FromBody] CreateMcpConnectionRequest request,
        CancellationToken ct)
    {
        try
        {
            var connection = await dispatcher.Send(
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
            return Ok(ToResponse(await dispatcher.Send(new TestMcpConnectionCommand(id), ct)));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpDelete("connections/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await dispatcher.Send(new DeleteMcpConnectionCommand(id), ct)
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
