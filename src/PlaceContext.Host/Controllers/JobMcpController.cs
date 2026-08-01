using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers;

[Authorize(Policy = Policies.DefaultAdmin)]
[Route("api/job-mcp/{projectId:guid}/{connectionName}")]
public sealed class JobMcpController : ControllerBase
{
    private readonly IMcpClientService _mcp;
    private readonly IMcpConnectionRepository _repo;

    public JobMcpController(IMcpClientService mcp, IMcpConnectionRepository repo)
    {
        _mcp = mcp;
        _repo = repo;
    }

    [HttpPost("call-tool")]
    public async Task<IActionResult> CallTool(Guid projectId, string connectionName, [FromBody] JsonElement body)
    {
        var conn = await ResolveConnectionAsync(projectId, connectionName);
        if (conn is null)
            return NotFound(new { error = $"MCP connection '{connectionName}' not found" });

        var toolName = body.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var arguments = body.TryGetProperty("arguments", out var a) ? a : (JsonElement?)null;

        var result = await _mcp.CallToolAsync(conn.Id, toolName, arguments);
        return Ok(new
        {
            success = result.Success,
            content = result.Content,
            error = result.Error,
            rawContent = result.RawContent,
        });
    }

    [HttpPost("list-tools")]
    public async Task<IActionResult> ListTools(Guid projectId, string connectionName)
    {
        var conn = await ResolveConnectionAsync(projectId, connectionName);
        if (conn is null)
            return NotFound(new { error = $"MCP connection '{connectionName}' not found" });

        var tools = await _mcp.ListToolsAsync(conn.Id);
        return Ok(new { tools });
    }

    private async Task<Domain.Entities.McpConnection?> ResolveConnectionAsync(Guid projectId, string connectionName)
    {
        var connections = await _repo.ListByProjectAsync(projectId);
        return connections.FirstOrDefault(c =>
            string.Equals(c.Name, connectionName, StringComparison.OrdinalIgnoreCase) && c.Enabled);
    }
}
