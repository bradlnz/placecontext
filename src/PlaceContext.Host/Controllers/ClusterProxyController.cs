using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace PlaceContext.Host.Controllers;

[Authorize]
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
        IOptions<ClusterProxyOptions> opts,
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
