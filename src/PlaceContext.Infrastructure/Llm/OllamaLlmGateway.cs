using System.Net.Http.Json;
using System.Text.Json;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.Configuration;

namespace PlaceContext.Infrastructure.Llm;

/// <summary>
/// Local-model <see cref="ILlmGateway"/> backed by an Ollama server (e.g. a small Gemma that fits in
/// ~16GB RAM). Selected when <c>PlaceContext:Llm:Provider = "ollama"</c>. Talks to Ollama's
/// <c>/api/generate</c> endpoint (non-streaming). Lets a self-hosted deployment organize job output
/// without sending data to a third-party API. Mirrors <see cref="AnthropicLlmGateway"/>.
/// </summary>
public sealed class OllamaLlmGateway : ILlmGateway
{
    private readonly IHttpClientFactory _http;
    private readonly string _endpoint;
    private readonly string _model;

    public OllamaLlmGateway(IHttpClientFactory http, IConfiguration config)
    {
        _http = http;
        var ollama = config.GetSection("PlaceContext:Llm:Ollama");
        _endpoint = (ollama["Endpoint"] ?? "http://localhost:11434").TrimEnd('/');
        _model = ollama["Model"] ?? "gemma3:4b";
    }

    // A local model is considered available whenever the Ollama provider is selected.
    public bool IsEnabled => true;

    public async Task<string> GenerateAsync(string system, string user, CancellationToken ct = default)
    {
        var client = _http.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/api/generate");
        req.Content = JsonContent.Create(new
        {
            model = _model,
            system,
            prompt = user,
            stream = false,
        });

        using var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        // Ollama /api/generate (non-streaming): { "response": "...", "done": true }
        var text = doc.RootElement.TryGetProperty("response", out var r) ? r.GetString() : null;
        return text ?? user;
    }
}
