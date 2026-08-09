using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Mcp.Controllers;

[ApiController]
[Authorize(Policy = Permission.SettingsManage)]
[Route("api/job-mcp/{projectId:guid}/{connectionName}")]
public sealed class JobMcpController(IMcpClientService mcp, IMcpConnectionRepository repository)
    : ControllerBase
{
    [HttpPost("call-tool")]
    public async Task<IActionResult> CallTool(
        Guid projectId,
        string connectionName,
        [FromBody] JsonElement body,
        CancellationToken ct)
    {
        var connection = await ResolveConnectionAsync(projectId, connectionName, ct);
        if (connection is null)
            return NotFound(new { error = $"MCP connection '{connectionName}' not found" });

        var toolName = body.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";
        var arguments = body.TryGetProperty("arguments", out var value) ? value : (JsonElement?)null;
        var result = await mcp.CallToolAsync(connection.Id, toolName, arguments, ct);
        return Ok(new
        {
            success = result.Success,
            content = result.Content,
            error = result.Error,
            rawContent = result.RawContent,
        });
    }

    [HttpPost("list-tools")]
    public async Task<IActionResult> ListTools(
        Guid projectId,
        string connectionName,
        CancellationToken ct)
    {
        var connection = await ResolveConnectionAsync(projectId, connectionName, ct);
        if (connection is null)
            return NotFound(new { error = $"MCP connection '{connectionName}' not found" });

        return Ok(new { tools = await mcp.ListToolsAsync(connection.Id, ct) });
    }

    private async Task<McpConnection?> ResolveConnectionAsync(
        Guid projectId,
        string connectionName,
        CancellationToken ct)
        => (await repository.ListByProjectAsync(projectId, ct)).FirstOrDefault(connection =>
            string.Equals(connection.Name, connectionName, StringComparison.OrdinalIgnoreCase)
            && connection.Enabled);
}
