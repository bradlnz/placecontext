using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;

namespace PlaceContext.AgentChat.Infrastructure.Slack;

public sealed class SlackApiClient : ISlackClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly IHttpClientFactory _http;
    private readonly SlackOptions _opts;

    public SlackApiClient(IHttpClientFactory http, IOptions<SlackOptions> opts)
    {
        _http = http;
        _opts = opts.Value;
    }

    public bool IsEnabled => _opts.IsConfigured;

    public async Task PostMessageAsync(string channel, string text, string? threadTs = null, CancellationToken ct = default)
    {
        if (!IsEnabled) return;

        var client = _http.CreateClient(nameof(SlackApiClient));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opts.BotToken);

        var body = new Dictionary<string, object?>
        {
            ["channel"] = channel,
            ["text"] = text,
        };
        if (!string.IsNullOrWhiteSpace(threadTs))
            body["thread_ts"] = threadTs;

        using var resp = await client.PostAsJsonAsync("https://slack.com/api/chat.postMessage", body, Json, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
            throw new InvalidOperationException($"Slack chat.postMessage failed: {err}");
        }
    }
}
