using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Mcp;

public sealed class McpClientService(
    IMcpConnectionRepository repository,
    IMcpUnitOfWork unitOfWork,
    IHttpClientFactory httpClientFactory,
    IDataEncryptor encryptor,
    ILogger<McpClientService> logger) : IMcpClientService
{
    private const string EncryptPurpose = "mcp.oauth.tokens";

    public async Task<IReadOnlyList<McpConnectionInfo>> ListConnectionsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var connections = await repository.ListByProjectAsync(projectId, cancellationToken);
        return connections
            .Select(connection => new McpConnectionInfo(
                connection.Id,
                connection.Name,
                connection.Transport,
                connection.EndpointUrl,
                connection.Enabled))
            .ToList();
    }

    public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var connection = await repository.GetByIdAsync(connectionId, cancellationToken);
        if (connection is null)
            return Array.Empty<McpToolDefinition>();

        try
        {
            var client = await CreateClientAsync(connection, cancellationToken);
            var request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/list",
                @params = new { },
            };
            var endpoint = GetEndpoint(connection);
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(request),
            };
            message.Headers.Accept.Add(new("*/*"));
            using var response = await client.SendAsync(message, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (!result.TryGetProperty("result", out var resultObject)
                || !resultObject.TryGetProperty("tools", out var tools))
            {
                return Array.Empty<McpToolDefinition>();
            }

            return tools.EnumerateArray()
                .Select(tool => new McpToolDefinition(
                    tool.GetProperty("name").GetString() ?? string.Empty,
                    tool.TryGetProperty("description", out var description) ? description.GetString() : null,
                    tool.TryGetProperty("inputSchema", out var schema) ? schema : null))
                .ToList();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to list tools from MCP server {ConnectionId}", connectionId);
            return Array.Empty<McpToolDefinition>();
        }
    }

    public async Task<McpToolResult> CallToolAsync(
        Guid connectionId,
        string toolName,
        JsonElement? arguments = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await repository.GetByIdAsync(connectionId, cancellationToken);
        if (connection is null)
            return new McpToolResult(false, null, "Connection not found");

        try
        {
            var client = await CreateClientAsync(connection, cancellationToken);
            var request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new
                {
                    name = toolName,
                    arguments = arguments ?? JsonDocument.Parse("{}").RootElement,
                },
            };
            var endpoint = GetEndpoint(connection);
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(request),
            };
            message.Headers.Accept.Add(new("*/*"));
            using var response = await client.SendAsync(message, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (result.TryGetProperty("result", out var resultObject))
            {
                if (resultObject.TryGetProperty("content", out var content))
                {
                    var text = content.EnumerateArray()
                        .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "text")
                        .Select(item => item.GetProperty("text").GetString())
                        .FirstOrDefault();
                    return new McpToolResult(true, text, null, content);
                }

                if (resultObject.TryGetProperty("isError", out var isError) && isError.GetBoolean())
                {
                    var error = resultObject.TryGetProperty("content", out var errorContent)
                        ? errorContent.ToString()
                        : "Unknown error";
                    return new McpToolResult(false, null, error);
                }
            }

            if (result.TryGetProperty("error", out var errorObject))
            {
                var error = errorObject.TryGetProperty("message", out var messageProperty)
                    ? messageProperty.GetString()
                    : errorObject.ToString();
                return new McpToolResult(false, null, error);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to call tool {ToolName} on MCP server {ConnectionId}",
                toolName,
                connectionId);
            return new McpToolResult(false, null, exception.Message);
        }

        return new McpToolResult(false, null, "Unexpected response format");
    }

    private async Task<HttpClient> CreateClientAsync(
        McpConnection connection,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        if (connection.AuthType == McpAuthType.OAuth)
        {
            var token = await ResolveOAuthTokenAsync(connection, cancellationToken);
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        }
        else if (!string.IsNullOrEmpty(connection.AuthToken))
        {
            if (connection.AuthType == McpAuthType.Bearer)
                client.DefaultRequestHeaders.Authorization = new("Bearer", connection.AuthToken);
            else if (connection.AuthType == McpAuthType.ApiKey)
                client.DefaultRequestHeaders.Add("X-API-Key", connection.AuthToken);
        }

        if (connection.AuthType != McpAuthType.OAuth
            && !string.IsNullOrEmpty(connection.AuthHeader)
            && !string.IsNullOrEmpty(connection.AuthToken))
        {
            client.DefaultRequestHeaders.Add(connection.AuthHeader, connection.AuthToken);
        }

        return client;
    }

    private async Task<string?> ResolveOAuthTokenAsync(
        McpConnection connection,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(connection.OAuthAccessToken))
            return null;
        if (!connection.OAuthTokenExpired)
            return encryptor.Unprotect(connection.OAuthAccessToken, EncryptPurpose);
        if (string.IsNullOrEmpty(connection.OAuthRefreshToken) || string.IsNullOrEmpty(connection.EndpointUrl))
            return null;

        try
        {
            var (tokenEndpoint, registrationEndpoint) = await DiscoverOAuthMetadataAsync(
                connection.EndpointUrl,
                cancellationToken);
            if (string.IsNullOrEmpty(tokenEndpoint))
                return null;

            var clientId = connection.OAuthClientId;
            if (string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(registrationEndpoint))
            {
                try
                {
                    clientId = await RegisterClientAsync(registrationEndpoint, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "MCP dynamic client registration failed");
                }
            }

            if (string.IsNullOrEmpty(clientId))
                return null;

            var refreshToken = encryptor.Unprotect(connection.OAuthRefreshToken, EncryptPurpose);
            if (string.IsNullOrEmpty(refreshToken))
                return null;

            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId,
            };
            using var response = await http.PostAsync(
                tokenEndpoint,
                new FormUrlEncodedContent(form),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var newAccessToken = json.GetProperty("access_token").GetString() ?? string.Empty;
            var newRefreshToken = json.TryGetProperty("refresh_token", out var refreshProperty)
                ? refreshProperty.GetString()
                : null;
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(json.GetProperty("expires_in").GetInt64());

            var encryptedAccess = encryptor.Protect(newAccessToken, EncryptPurpose);
            var encryptedRefresh = string.IsNullOrEmpty(newRefreshToken)
                ? connection.OAuthRefreshToken
                : encryptor.Protect(newRefreshToken, EncryptPurpose);

            connection.StoreOAuthTokens(encryptedAccess, encryptedRefresh, expiresAt, DateTimeOffset.UtcNow);
            connection.RecordConnection("oauth:connected", DateTimeOffset.UtcNow);
            await repository.UpdateAsync(connection, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return newAccessToken;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "OAuth token refresh failed for connection {ConnectionId}", connection.Id);
            return null;
        }
    }

    private async Task<(string? TokenEndpoint, string? RegistrationEndpoint)> DiscoverOAuthMetadataAsync(
        string endpointUrl,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(endpointUrl);
        var baseUri = $"{uri.Scheme}://{uri.Authority}";
        var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);

        try
        {
            using var response = await http.GetAsync(
                $"{baseUri}/.well-known/oauth-authorization-server",
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                return (null, null);

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return (
                json.TryGetProperty("token_endpoint", out var tokenEndpoint)
                    ? tokenEndpoint.GetString()
                    : null,
                json.TryGetProperty("registration_endpoint", out var registrationEndpoint)
                    ? registrationEndpoint.GetString()
                    : null);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "MCP OAuth metadata discovery failed for {Endpoint}", endpointUrl);
            return (null, null);
        }
    }

    private async Task<string> RegisterClientAsync(
        string registrationEndpoint,
        CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);
        var body = JsonSerializer.Serialize(new
        {
            redirect_uris = Array.Empty<string>(),
            client_name = "PlaceContext",
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(registrationEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return json.GetProperty("client_id").GetString()
            ?? throw new InvalidOperationException("No client_id in DCR response.");
    }

    private static string GetEndpoint(McpConnection connection) =>
        connection.EndpointUrl?.TrimEnd('/')
        ?? throw new InvalidOperationException("No endpoint URL configured");
}
