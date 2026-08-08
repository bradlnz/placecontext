using System.Net.Http.Json;
using System.Text.Json;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PlaceContext.AgentChat.Infrastructure.Chat;

/// <summary>
/// Platform-agnostic cluster gateway: talks to the ClusterHost sidecar
/// (<c>http://localhost:8081/api/cluster</c>) which proxies to shard servers.
///
/// The sidecar runs independently in the same pod, so the Host doesn't depend on
/// the cluster being available to start. The gateway probes the sidecar's health
/// endpoint and only reports enabled when shards are reachable.
/// </summary>
public sealed class ClusterChatGateway : IChatGateway, IDisposable
{
    private readonly IHttpClientFactory _http;
    private readonly string _endpoint;
    private readonly ILogger<ClusterChatGateway> _log;
    private readonly PeriodicTimer? _healthTimer;
    private volatile bool _clusterHealthy;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public ClusterChatGateway(IHttpClientFactory http, IConfiguration config, ILogger<ClusterChatGateway> log)
    {
        _http = http;
        _log = log;
        var section = config.GetSection("PlaceContext:ClusterChat");
        _endpoint = (section["Endpoint"] ?? "").TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(_endpoint))
        {
            _log.LogInformation("ClusterChatGateway initializing: endpoint={Endpoint}", _endpoint);
            _ = ProbeHealthAsync(CancellationToken.None);
            _healthTimer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            _ = RunHealthLoopAsync();
        }
        else
        {
            _log.LogWarning("ClusterChatGateway: no endpoint configured");
        }
    }

    /// <summary>
    /// True when the endpoint is configured AND the cluster sidecar reports healthy shards.
    /// </summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(_endpoint) && _clusterHealthy;

    /// <summary>Human-readable status for the UI.</summary>
    public string StatusText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_endpoint)) return "No cluster configured";
            return _clusterHealthy ? "Cluster connected" : "Cluster connecting…";
        }
    }

    public async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages, ChatSettings? settings = null, CancellationToken ct = default)
    {
        if (messages.Count == 0) return string.Empty;

        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        var url = $"{_endpoint}/chat";

        var payload = new ClusterChatRequestDto
        {
            Messages = messages.Select(message => new ClusterChatMessageDto
            {
                Role = message.Role,
                Content = message.Content,
            }).ToList(),
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

    public async IAsyncEnumerable<string> ChatStreamAsync(
        IReadOnlyList<ChatMessage> messages, ChatSettings? settings = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (messages.Count == 0) yield break;

        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(10);
        var url = $"{_endpoint}/chat/stream";

        var payload = new ClusterChatRequestDto
        {
            Messages = messages.Select(message => new ClusterChatMessageDto
            {
                Role = message.Role,
                Content = message.Content,
            }).ToList(),
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
            catch (JsonException) { }

            if (!string.IsNullOrEmpty(content))
                yield return content;
        }
    }

    private async Task ProbeHealthAsync(CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var url = $"{_endpoint}/health";
            _log.LogDebug("Health probe: GET {Url}", url);
            var resp = await client.GetAsync(url, ct);
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("status", out var status) && status.GetString() == "ok")
                {
                    var healthy = doc.RootElement.TryGetProperty("healthy", out var h) ? h.GetInt32() : 0;
                    var total = doc.RootElement.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
                    var wasHealthy = _clusterHealthy;
                    _clusterHealthy = healthy > 0;
                    if (!wasHealthy && _clusterHealthy)
                        _log.LogInformation("Cluster available: {Healthy}/{Total} shards", healthy, total);
                    else if (wasHealthy && !_clusterHealthy)
                        _log.LogWarning("Cluster lost: {Healthy}/{Total} shards", healthy, total);
                    else
                        _log.LogDebug("Cluster health check: {Healthy}/{Total} (enabled={Enabled})", healthy, total, _clusterHealthy);
                }
                else
                {
                    _log.LogWarning("Cluster health response missing status=ok: {Body}", body.Length > 200 ? body[..200] : body);
                }
            }
            else
            {
                _log.LogWarning("Cluster health probe returned {StatusCode}", resp.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Cluster health probe failed (wasHealthy={WasHealthy})", _clusterHealthy);
        }
    }

    private async Task RunHealthLoopAsync()
    {
        if (_healthTimer is null) return;
        while (await _healthTimer.WaitForNextTickAsync())
        {
            await ProbeHealthAsync(CancellationToken.None);
        }
    }

    public void Dispose() => _healthTimer?.Dispose();

}
