using System.Net.Http.Json;
using System.Text.Json;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.Configuration;

namespace PlaceContext.Infrastructure.Llm;

/// <summary>
/// Anthropic-backed <see cref="ILlmGateway"/> used to polish assembled reports into prose. Mirrors the
/// <c>GitHubGateway</c> pattern (an <see cref="IHttpClientFactory"/> + config). Only registered when an
/// API key is configured (<c>PlaceContext:Llm:ApiKey</c>); otherwise the Null gateway is used.
/// </summary>
public sealed class AnthropicLlmGateway : ILlmGateway
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";

    private readonly IHttpClientFactory _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxTokens;

    public AnthropicLlmGateway(IHttpClientFactory http, IConfiguration config)
    {
        _http = http;
        var llm = config.GetSection("PlaceContext:Llm");
        _apiKey = llm["ApiKey"] ?? "";
        _model = llm["Model"] ?? "claude-sonnet-4-6";
        _maxTokens = int.TryParse(llm["MaxTokens"], out var m) ? m : 4096;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<string> GenerateAsync(string system, string user, CancellationToken ct = default)
    {
        var client = _http.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = JsonContent.Create(new
        {
            model = _model,
            max_tokens = _maxTokens,
            system,
            messages = new[] { new { role = "user", content = user } }
        });

        using var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        // Messages API: { content: [ { type: "text", text: "..." }, ... ] }
        var text = doc.RootElement.GetProperty("content").EnumerateArray()
            .Where(b => b.GetProperty("type").GetString() == "text")
            .Select(b => b.GetProperty("text").GetString())
            .FirstOrDefault();
        return text ?? user;
    }
}
