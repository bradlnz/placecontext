using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Mcp;

public sealed class McpClientService : IMcpClientService
{
    private readonly IMcpConnectionRepository _repo;
    private readonly IHttpClientFactory _http;
    private readonly IDataEncryptor _encryptor;
    private readonly ILogger<McpClientService> _log;
    private static readonly string EncryptPurpose = "mcp.oauth.tokens";

    public McpClientService(
        IMcpConnectionRepository repo,
        IHttpClientFactory http,
        IDataEncryptor encryptor,
        ILogger<McpClientService> log)
    {
        _repo = repo;
        _http = http;
        _encryptor = encryptor;
        _log = log;
    }

    public async Task<IReadOnlyList<McpConnectionInfo>> ListConnectionsAsync(Guid projectId, CancellationToken ct = default)
    {
        var connections = await _repo.ListByProjectAsync(projectId, ct);
        return connections.Select(c => new McpConnectionInfo(c.Id, c.Name, c.Transport, c.EndpointUrl, c.Enabled)).ToList();
    }

    public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(Guid connectionId, CancellationToken ct = default)
    {
        var connection = await _repo.GetByIdAsync(connectionId, ct);
        if (connection == null) return Array.Empty<McpToolDefinition>();

        try
        {
            var client = CreateClient(connection);
            var request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/list",
                @params = new { }
            };

            var response = await client.PostAsJsonAsync(GetEndpoint(connection), request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (result.TryGetProperty("result", out var resultObj) && resultObj.TryGetProperty("tools", out var tools))
            {
                var definitions = new List<McpToolDefinition>();
                foreach (var tool in tools.EnumerateArray())
                {
                    var name = tool.GetProperty("name").GetString() ?? "";
                    var description = tool.TryGetProperty("description", out var desc) ? desc.GetString() : null;
                    JsonElement? schema = tool.TryGetProperty("inputSchema", out var s) ? s : null;
                    definitions.Add(new McpToolDefinition(name, description, schema));
                }
                return definitions;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to list tools from MCP server {ConnectionId}", connectionId);
        }

        return Array.Empty<McpToolDefinition>();
    }

    public async Task<McpToolResult> CallToolAsync(Guid connectionId, string toolName, JsonElement? arguments = null, CancellationToken ct = default)
    {
        var connection = await _repo.GetByIdAsync(connectionId, ct);
        if (connection == null) return new McpToolResult(false, null, "Connection not found");

        try
        {
            var client = CreateClient(connection);
            var request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new
                {
                    name = toolName,
                    arguments = arguments ?? JsonDocument.Parse("{}").RootElement
                }
            };

            var response = await client.PostAsJsonAsync(GetEndpoint(connection), request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (result.TryGetProperty("result", out var resultObj))
            {
                if (resultObj.TryGetProperty("content", out var content))
                {
                    var text = content.EnumerateArray()
                        .Where(c => c.TryGetProperty("type", out var t) && t.GetString() == "text")
                        .Select(c => c.GetProperty("text").GetString())
                        .FirstOrDefault();
                    return new McpToolResult(true, text, null);
                }
                if (resultObj.TryGetProperty("isError", out var isError) && isError.GetBoolean())
                {
                    var error = resultObj.TryGetProperty("content", out var errContent)
                        ? errContent.ToString()
                        : "Unknown error";
                    return new McpToolResult(false, null, error);
                }
            }
            if (result.TryGetProperty("error", out var errorObj))
            {
                var errorMsg = errorObj.TryGetProperty("message", out var msg) ? msg.GetString() : errorObj.ToString();
                return new McpToolResult(false, null, errorMsg);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to call tool {ToolName} on MCP server {ConnectionId}", toolName, connectionId);
            return new McpToolResult(false, null, ex.Message);
        }

        return new McpToolResult(false, null, "Unexpected response format");
    }

    private HttpClient CreateClient(McpConnection connection)
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        if (connection.AuthType == McpAuthType.OAuth)
        {
            var token = ResolveOAuthToken(connection);
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
        }
        else if (!string.IsNullOrEmpty(connection.AuthToken))
        {
            if (connection.AuthType == McpAuthType.Bearer)
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", connection.AuthToken);
            }
            else if (connection.AuthType == McpAuthType.ApiKey)
            {
                client.DefaultRequestHeaders.Add("X-API-Key", connection.AuthToken);
            }
        }

        if (connection.AuthType != McpAuthType.OAuth
            && !string.IsNullOrEmpty(connection.AuthHeader)
            && !string.IsNullOrEmpty(connection.AuthToken))
        {
            client.DefaultRequestHeaders.Add(connection.AuthHeader, connection.AuthToken);
        }

        return client;
    }

    private string? ResolveOAuthToken(McpConnection connection)
    {
        if (string.IsNullOrEmpty(connection.OAuthAccessToken))
            return null;

        return _encryptor.Unprotect(connection.OAuthAccessToken, EncryptPurpose);
    }

    private string GetEndpoint(McpConnection connection)
    {
        return connection.EndpointUrl?.TrimEnd('/') ?? throw new InvalidOperationException("No endpoint URL configured");
    }
}
