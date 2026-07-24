using System.Net.Http.Json;
using System.Text.Json;

namespace PlaceContext.ClusterHost;

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

        yield return "[error: multi-shard pipeline not yet supported in ClusterHost]";
    }

    private async IAsyncEnumerable<string> StreamSingleShard(
        HttpClient client, string url, ClusterChatRequest req,
        float temp, float topP, int maxTokens,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
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
