using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient();

// Configure cluster options from environment variables
builder.Services.Configure<ClusterProxyOptions>(builder.Configuration.GetSection("PlaceContext:ClusterChat"));

builder.Services.AddSingleton<ClusterProxyService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ClusterProxyService>());
builder.Services.AddScoped<ClusterPipeline>();

var app = builder.Build();

// Log startup
var shardEndpoints = builder.Configuration.GetSection("PlaceContext:ClusterChat:ShardEndpoints").Get<List<string>>() ?? new();
var model = builder.Configuration["PlaceContext:ClusterChat:Model"] ?? "qwen3.5-4b";
Console.WriteLine($"[ClusterHost] Starting on port {builder.Configuration["ASPNETCORE_HTTP_PORTS"] ?? "8081"}");
Console.WriteLine($"[ClusterHost] Model: {model}");
Console.WriteLine($"[ClusterHost] Shard endpoints: [{string.Join(", ", shardEndpoints)}]");

app.MapControllers();
app.Run();

// ── Controllers ──────────────────────────────────────────────────────────

[ApiController]
[Route("api/cluster")]
public sealed class ClusterProxyController : ControllerBase
{
    private readonly ClusterPipeline _pipeline;
    private readonly ClusterProxyService _svc;
    private readonly ClusterProxyOptions _opts;
    private readonly ILogger<ClusterProxyController> _log;

    public ClusterProxyController(
        ClusterPipeline pipeline,
        ClusterProxyService svc,
        Microsoft.Extensions.Options.IOptions<ClusterProxyOptions> opts,
        ILogger<ClusterProxyController> log)
    {
        _pipeline = pipeline;
        _svc = svc;
        _opts = opts.Value;
        _log = log;
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health()
    {
        var healthy = _svc.HealthyEndpoints;
        return Ok(new { status = healthy.Count > 0 ? "ok" : "no_shards", healthy = healthy.Count, total = _opts.ShardEndpoints.Count, endpoints = healthy, model = _opts.Model });
    }

    [HttpPost("chat")]
    [AllowAnonymous]
    public async Task<IActionResult> Chat([FromBody] ClusterChatRequest req)
    {
        try
        {
            var text = await _pipeline.GenerateAsync(req, HttpContext.RequestAborted);
            return Ok(new
            {
                id = $"chatcmpl-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                @object = "chat.completion",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                model = _opts.Model,
                choices = new[] { new { index = 0, message = new { role = "assistant", content = text }, finish_reason = "stop" } },
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Pipeline chat failed");
            return StatusCode(502, new { error = ex.Message });
        }
    }

    [HttpPost("chat/stream")]
    [AllowAnonymous]
    public async Task ChatStream([FromBody] ClusterChatRequest req)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        try
        {
            await foreach (var token in _pipeline.GenerateStreamAsync(req, HttpContext.RequestAborted))
            {
                var chunk = new { choices = new[] { new { delta = new { content = token }, finish_reason = (string?)null } } };
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n", HttpContext.RequestAborted);
                await Response.Body.FlushAsync(HttpContext.RequestAborted);
            }
            await Response.WriteAsync("data: [DONE]\n\n", HttpContext.RequestAborted);
            await Response.Body.FlushAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Pipeline stream failed");
            try { await Response.WriteAsync($"data: {{\"error\":\"{ex.Message}\"}}\n\ndata: [DONE]\n\n"); } catch { }
        }
    }
}

public sealed class ClusterChatRequest
{
    [JsonPropertyName("messages")] public List<ClusterMessageDto> Messages { get; set; } = new();
    [JsonPropertyName("temperature")] public float? Temperature { get; set; }
    [JsonPropertyName("top_p")] public float? TopP { get; set; }
    [JsonPropertyName("max_tokens")] public int? MaxTokens { get; set; }
}

public sealed class ClusterMessageDto
{
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}

public sealed class ClusterProxyOptions
{
    public List<string> ShardEndpoints { get; set; } = new();
    public string Model { get; set; } = "qwen3.5-4b";
}

// ── ClusterPipeline ─────────────────────────────────────────────────────

public sealed class ClusterPipeline
{
    private readonly IHttpClientFactory _http;
    private readonly ClusterProxyOptions _opts;
    private readonly ILogger<ClusterPipeline> _log;
    private static readonly JsonSerializerOptions J = new() { PropertyNameCaseInsensitive = true };

    public ClusterPipeline(
        IHttpClientFactory http,
        Microsoft.Extensions.Options.IOptions<ClusterProxyOptions> opts,
        ILogger<ClusterPipeline> log)
    {
        _http = http;
        _opts = opts.Value;
        _log = log;
        _log.LogInformation("ClusterPipeline initialized: shards={Count}, model={Model}", _opts.ShardEndpoints.Count, _opts.Model);
    }

    public bool IsEnabled => _opts.ShardEndpoints.Count > 0;

    public async Task<string> GenerateAsync(ClusterChatRequest req, CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
        await foreach (var tok in GenerateStreamAsync(req, ct))
            sb.Append(tok);
        return sb.ToString();
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(
        ClusterChatRequest req,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var shards = _opts.ShardEndpoints.Select(e => e.TrimEnd('/')).ToArray();
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);

        var temp = req.Temperature ?? 0.7f;
        var topP = req.TopP ?? 0.9f;
        var maxTokens = req.MaxTokens ?? 2048;

        if (shards.Length == 1)
        {
            await foreach (var token in StreamSingleShard(client, shards[0], req, temp, topP, maxTokens, ct))
                yield return token;
            yield break;
        }

        // Multi-shard pipeline mode (omitted for brevity — single shard is the common case)
        yield return "[error: multi-shard pipeline not yet supported in ClusterHost]";
    }

    private async IAsyncEnumerable<string> StreamSingleShard(
        HttpClient client, string url, ClusterChatRequest req,
        float temp, float topP, int maxTokens, CancellationToken ct)
    {
        var payload = new
        {
            model = _opts.Model,
            messages = req.Messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
            stream = true,
            temperature = temp,
            top_p = topP,
            max_tokens = maxTokens,
        };

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{url}/v1/chat/stream")
        { Content = JsonContent.Create(payload, options: J) };

        HttpResponseMessage? resp = null;
        string? requestError = null;
        try
        {
            resp = await client.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Stream request failed for {Url}", url);
            requestError = ex.Message;
        }

        if (requestError is not null)
        {
            yield return $"[stream error: {requestError}]";
            yield break;
        }

        if (resp is null) yield break;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct);
            }
            catch (IOException)
            {
                break;
            }

            if (line is null) break;
            if (!line.StartsWith("data: ")) continue;
            var data = line[6..];
            if (data == "[DONE]") break;
            if (string.IsNullOrWhiteSpace(data)) continue;

            string? content = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var first = choices[0];
                    if (first.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var contentEl))
                        content = contentEl.GetString();
                    if (first.TryGetProperty("finish_reason", out var finish) && finish.GetString() == "stop")
                        break;
                }
            }
            catch (JsonException) { }

            if (!string.IsNullOrEmpty(content)) yield return content;
        }
    }
}

// ── ClusterProxyService (health checker) ─────────────────────────────────

public sealed class ClusterProxyService : BackgroundService
{
    private readonly IHttpClientFactory _http;
    private readonly ClusterProxyOptions _opts;
    private readonly ILogger<ClusterProxyService> _log;
    private readonly Dictionary<string, bool> _health = new();

    public IReadOnlyList<string> HealthyEndpoints
    {
        get
        {
            lock (_health)
                return _health.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
        }
    }

    public ClusterProxyService(
        IHttpClientFactory http,
        Microsoft.Extensions.Options.IOptions<ClusterProxyOptions> opts,
        ILogger<ClusterProxyService> log)
    {
        _http = http;
        _opts = opts.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial check after 3 seconds
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        await CheckHealthAsync(stoppingToken);

        // Then every 15 seconds
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            await CheckHealthAsync(stoppingToken);
        }
    }

    private async Task CheckHealthAsync(CancellationToken ct)
    {
        var endpoints = _opts.ShardEndpoints.Count > 0
            ? _opts.ShardEndpoints.ToArray()
            : Array.Empty<string>();

        if (endpoints.Length == 0) return;

        var nowHealthy = new List<string>();
        foreach (var ep in endpoints)
        {
            try
            {
                var client = _http.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var resp = await client.GetAsync($"{ep}/health", ct);
                if (resp.IsSuccessStatusCode)
                {
                    nowHealthy.Add(ep);
                    _health[ep] = true;
                }
                else
                {
                    _health[ep] = false;
                }
            }
            catch
            {
                _health[ep] = false;
            }
        }

        if (nowHealthy.Count > 0)
            _log.LogInformation("Cluster healthy: {Healthy}/{Total} shards available", nowHealthy.Count, endpoints.Length);
        else
            _log.LogWarning("No shard servers healthy");
    }
}
