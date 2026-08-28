using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PlaceContext.ClusterHost;

[ApiController]
[Route("api/cluster")]
public sealed class ClusterProxyController : ControllerBase
{
    private const int MaxMessages = 64;
    private const int MaxMessageChars = 32_768;
    private const int MaxPromptChars = 131_072;
    private const int MaxTokens = 4_096;
    private const int MaxEmbeddingInputs = 32;
    private const int MaxEmbeddingChars = 4_000;

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
        return Ok(new { status = healthy.Count > 0 ? "ok" : "no_shards", healthy = healthy.Count, total = _opts.ShardEndpoints.Count, model = _opts.Model });
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ClusterChatRequest req)
    {
        if (Validate(req) is { } error)
            return BadRequest(new { error });

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
            return StatusCode(502, new { error = "The cluster chat request failed." });
        }
    }

    [HttpPost("embeddings")]
    public async Task<IActionResult> Embeddings([FromBody] ClusterEmbedRequest req)
    {
        if (req.Input.Count is 0 or > MaxEmbeddingInputs
            || req.Input.Any(text => string.IsNullOrWhiteSpace(text) || text.Length > MaxEmbeddingChars))
        {
            return BadRequest(new
            {
                error = $"input must contain 1-{MaxEmbeddingInputs} non-empty strings of at most {MaxEmbeddingChars} characters."
            });
        }

        try
        {
            var vectors = await _pipeline.EmbedAsync(req.Input, HttpContext.RequestAborted);
            return Ok(new { vectors, dimensions = vectors.Count > 0 ? vectors[0].Length : 0 });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Pipeline embeddings failed");
            return StatusCode(502, new { error = "The cluster embedding request failed." });
        }
    }

    [HttpPost("chat/stream")]
    public async Task ChatStream([FromBody] ClusterChatRequest req)
    {
        if (Validate(req) is { } validationError)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { error = validationError }, HttpContext.RequestAborted);
            return;
        }

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
            try
            {
                var error = JsonSerializer.Serialize(new { error = "The cluster streaming request failed." });
                await Response.WriteAsync($"data: {error}\n\ndata: [DONE]\n\n");
            }
            catch { }
        }
    }

    private static string? Validate(ClusterChatRequest req)
    {
        if (req.Messages.Count is 0 or > MaxMessages)
            return $"messages must contain 1-{MaxMessages} entries.";
        if (req.Messages.Any(message =>
                message.Role is not ("system" or "user" or "assistant")
                || string.IsNullOrWhiteSpace(message.Content)
                || message.Content.Length > MaxMessageChars))
        {
            return $"Each message must use a supported role and contain at most {MaxMessageChars} characters.";
        }
        if (req.Messages.Sum(message => (long)message.Content.Length) > MaxPromptChars)
            return $"The combined message content must not exceed {MaxPromptChars} characters.";
        if (req.MaxTokens is < 1 or > MaxTokens)
            return $"max_tokens must be between 1 and {MaxTokens}.";
        if (req.Temperature is < 0 or > 2)
            return "temperature must be between 0 and 2.";
        if (req.TopP is <= 0 or > 1)
            return "top_p must be greater than 0 and at most 1.";
        return null;
    }
}
