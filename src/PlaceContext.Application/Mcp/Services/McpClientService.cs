using System.Net.Http.Json;
using System.Text;
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
    private readonly IUnitOfWork _uow;
    private readonly IHttpClientFactory _http;
    private readonly IDataEncryptor _encryptor;
    private readonly ILogger<McpClientService> _log;
    private static readonly string EncryptPurpose = "mcp.oauth.tokens";

    public McpClientService(
        IMcpConnectionRepository repo,
        IUnitOfWork uow,
        IHttpClientFactory http,
        IDataEncryptor encryptor,
        ILogger<McpClientService> log)
    {
        _repo = repo;
        _uow = uow;
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
            var client = await CreateClientAsync(connection, ct);
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
            var client = await CreateClientAsync(connection, ct);
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

    private async Task<HttpClient> CreateClientAsync(McpConnection connection, CancellationToken ct)
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        if (connection.AuthType == McpAuthType.OAuth)
        {
            var token = await ResolveOAuthTokenAsync(connection, ct);
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

    private async Task<string?> ResolveOAuthTokenAsync(McpConnection connection, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(connection.OAuthAccessToken))
            return null;

        if (!connection.OAuthTokenExpired)
            return _encryptor.Unprotect(connection.OAuthAccessToken, EncryptPurpose);

        if (string.IsNullOrEmpty(connection.OAuthRefreshToken) || string.IsNullOrEmpty(connection.EndpointUrl))
            return null;

        try
        {
            var (tokenEndpoint, regEndpoint) = await DiscoverOAuthMetadataAsync(connection.EndpointUrl, ct);
            if (string.IsNullOrEmpty(tokenEndpoint))
                return null;

            var clientId = connection.OAuthClientId;
            if (string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(regEndpoint))
            {
                try { clientId = await RegisterClientAsync(regEndpoint, ct); }
                catch { }
            }
            if (string.IsNullOrEmpty(clientId))
                return null;

            var refreshToken = _encryptor.Unprotect(connection.OAuthRefreshToken, EncryptPurpose);
            if (string.IsNullOrEmpty(refreshToken))
                return null;

            var http = _http.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId,
            };
            var resp = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            var newAccessToken = json.GetProperty("access_token").GetString() ?? "";
            var newRefreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            var expiresIn = json.GetProperty("expires_in").GetInt64();
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

            var encryptedAccess = _encryptor.Protect(newAccessToken, EncryptPurpose);
            var encryptedRefresh = string.IsNullOrEmpty(newRefreshToken)
                ? connection.OAuthRefreshToken
                : _encryptor.Protect(newRefreshToken, EncryptPurpose);

            connection.StoreOAuthTokens(encryptedAccess, encryptedRefresh, expiresAt, DateTimeOffset.UtcNow);
            connection.RecordConnection("oauth:connected", DateTimeOffset.UtcNow);
            await _repo.UpdateAsync(connection, ct);
            await _uow.SaveChangesAsync(ct);

            _log.LogInformation("OAuth token refreshed for connection {ConnectionId}", connection.Id);
            return newAccessToken;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "OAuth token refresh failed for connection {ConnectionId}", connection.Id);
            return null;
        }
    }

    private async Task<(string? tokenEndpoint, string? registrationEndpoint)> DiscoverOAuthMetadataAsync(string endpointUrl, CancellationToken ct)
    {
        var uri = new Uri(endpointUrl);
        var baseUri = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        var http = _http.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);

        try
        {
            var metaResp = await http.GetAsync($"{baseUri}/.well-known/oauth-authorization-server", ct);
            if (metaResp.IsSuccessStatusCode)
            {
                var metaJson = await metaResp.Content.ReadFromJsonAsync<JsonElement>(ct);
                var tokenEndpoint = metaJson.TryGetProperty("token_endpoint", out var te) ? te.GetString() : null;
                var regEndpoint = metaJson.TryGetProperty("registration_endpoint", out var re) ? re.GetString() : null;
                return (tokenEndpoint, regEndpoint);
            }
        }
        catch { }

        return (null, null);
    }

    private async Task<string> RegisterClientAsync(string registrationEndpoint, CancellationToken ct)
    {
        var http = _http.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);

        var body = JsonSerializer.Serialize(new
        {
            redirect_uris = Array.Empty<string>(),
            client_name = "PlaceContext",
        });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await http.PostAsync(registrationEndpoint, content, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("client_id").GetString()
            ?? throw new InvalidOperationException("No client_id in DCR response.");
    }

    private string GetEndpoint(McpConnection connection)
    {
        return connection.EndpointUrl?.TrimEnd('/') ?? throw new InvalidOperationException("No endpoint URL configured");
    }
}
