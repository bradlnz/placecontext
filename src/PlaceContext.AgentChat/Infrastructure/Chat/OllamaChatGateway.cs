using System.Net.Http.Json;
using System.Text.Json;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.Configuration;

namespace PlaceContext.AgentChat.Infrastructure.Chat;

/// <summary>
/// Ollama-backed <see cref="IChatGateway"/>. Registered only when <c>PlaceContext:Chat:Endpoint</c>
/// is set; otherwise the Null gateway is used. Uses <see cref="IHttpClientFactory"/> + config.
/// </summary>
public sealed class OllamaChatGateway : IChatGateway
{
    private readonly IHttpClientFactory _http;
    private readonly string _endpoint;
    private readonly string _model;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public OllamaChatGateway(IHttpClientFactory http, IConfiguration config)
    {
        _http = http;
        var section = config.GetSection("PlaceContext:Chat");
        _endpoint = (section["Endpoint"] ?? "http://localhost:11434").TrimEnd('/');
        _model = section["Model"] ?? "qwen3.5:0.8b";
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_endpoint);

    public async Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, ChatSettings? settings = null, CancellationToken ct = default)
    {
        if (messages.Count == 0) return string.Empty;

        var client = _http.CreateClient();
        var url = $"{_endpoint}/api/chat";

        var payload = new
        {
            model = _model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
            stream = false,
            options = new
            {
                temperature = settings?.Temperature,
                top_p = settings?.TopP,
                num_predict = settings?.MaxTokens,
            },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, options: Json),
        };

        using var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }
}
