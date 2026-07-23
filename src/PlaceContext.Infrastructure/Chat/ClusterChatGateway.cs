using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PlaceContext.Infrastructure.Chat;

/// <summary>
/// Platform-agnostic cluster gateway: talks to the ClusterProxyController
/// (<c>/api/cluster/chat</c>) which routes to shard servers running SafeTensors models
/// on Mac/Linux nodes. The gateway never contacts shard servers directly.
///
/// Registered when <c>PlaceContext:ClusterChat:Endpoint</c> is set; replaces the Ollama gateway.
/// In single-node mode the endpoint points to one shard server running the full model.
/// In pipeline mode a future coordinator URL chains multiple shards transparently.
/// </summary>
public sealed class ClusterChatGateway : IChatGateway
{
    private readonly IHttpClientFactory _http;
    private readonly string _endpoint;
    private readonly ILogger<ClusterChatGateway> _log;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public ClusterChatGateway(IHttpClientFactory http, IConfiguration config, ILogger<ClusterChatGateway> log)
    {
        _http = http;
        _log = log;
        var section = config.GetSection("PlaceContext:ClusterChat");
        // Endpoint is the proxy base URL (e.g. "http://localhost/api/cluster" or "/api/cluster" for same-origin).
        _endpoint = (section["Endpoint"] ?? "").TrimEnd('/');
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_endpoint);

    public async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages, ChatSettings? settings = null, CancellationToken ct = default)
    {
        if (messages.Count == 0) return string.Empty;

        var client = _http.CreateClient();
        var url = $"{_endpoint}/chat";

        var payload = new ClusterChatDto
        {
            Messages = messages.Select(m => new MsgDto { Role = m.Role, Content = m.Content }).ToList(),
            Temperature = settings?.Temperature,
            TopP = settings?.TopP,
            MaxTokens = settings?.MaxTokens,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, options: Json),
        };

        try
        {
            using var resp = await client.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var contentEl))
                    return contentEl.GetString() ?? string.Empty;
            }
            return string.Empty;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Cluster chat request failed (endpoint={Endpoint})", _endpoint);
            return $"[cluster error: {ex.Message}]";
        }
    }

    /// <summary>
    /// Streaming chat: yields tokens as they arrive from the proxy → shard server via SSE.
    /// The chat UI subscribes to this for real-time response rendering.
    /// </summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        IReadOnlyList<ChatMessage> messages, ChatSettings? settings = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (messages.Count == 0) yield break;

        var client = _http.CreateClient();
        var url = $"{_endpoint}/chat/stream";

        var payload = new ClusterChatDto
        {
            Messages = messages.Select(m => new MsgDto { Role = m.Role, Content = m.Content }).ToList(),
            Temperature = settings?.Temperature,
            TopP = settings?.TopP,
            MaxTokens = settings?.MaxTokens,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, options: Json),
        };

        HttpResponseMessage? resp = null;
        try
        {
            resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Cluster stream request failed (endpoint={Endpoint})", _endpoint);
        }

        if (resp is null)
        {
            yield return "[cluster error: request failed]";
            yield break;
        }

        await foreach (var token in ReadStreamTokens(resp, ct))
        {
            yield return token;
        }
    }

    private static async IAsyncEnumerable<string> ReadStreamTokens(
        HttpResponseMessage resp,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var respDispose = resp;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            // SSE format: "data: {...}" or "data: [DONE]"
            if (!line.StartsWith("data: ")) continue;
            var data = line[6..];
            if (data == "[DONE]") break;

            string? content = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var first = choices[0];
                    if (first.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var contentEl))
                        content = contentEl.GetString();
                }
            }
            catch (JsonException)
            {
                // Skip malformed SSE chunks
            }

            if (!string.IsNullOrEmpty(content))
                yield return content;
        }
    }

    // DTOs matching the ClusterProxyController contract

    private sealed class ClusterChatDto
    {
        [JsonPropertyName("messages")] public List<MsgDto> Messages { get; set; } = new();
        [JsonPropertyName("temperature")] public float? Temperature { get; set; }
        [JsonPropertyName("top_p")] public float? TopP { get; set; }
        [JsonPropertyName("max_tokens")] public int? MaxTokens { get; set; }
    }

    private sealed class MsgDto
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }
}
