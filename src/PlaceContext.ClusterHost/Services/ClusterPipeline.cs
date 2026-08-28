using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace PlaceContext.ClusterHost;

public sealed class ClusterPipeline
{
    private const int MaxGeneratedTokens = 4_096;
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
        _log.LogInformation(
            "ClusterPipeline initialized: shards={Count}, model={Model}",
            _opts.ShardEndpoints.Count,
            _opts.Model);
    }

    public bool IsEnabled => _opts.ShardEndpoints.Count > 0;

    public async Task<string> GenerateAsync(ClusterChatRequest req, CancellationToken ct)
    {
        var output = new System.Text.StringBuilder();
        await foreach (var token in GenerateStreamAsync(req, ct))
            output.Append(token);
        return output.ToString();
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(
        ClusterChatRequest req,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var shards = NormalizedShards();
        if (shards.Length == 0)
            throw new InvalidOperationException("No shard endpoints are configured.");

        var client = _http.CreateClient();
        AddAuthentication(client);
        client.Timeout = TimeSpan.FromMinutes(10);

        var temperature = Math.Max(0, req.Temperature ?? 0.7f);
        var topP = Math.Clamp(req.TopP ?? 0.9f, 0.0001f, 1f);
        var maxTokens = Math.Clamp(req.MaxTokens ?? 2048, 1, MaxGeneratedTokens);

        if (shards.Length == 1)
        {
            await foreach (var token in StreamSingleShard(
                client, shards[0], req, temperature, topP, maxTokens, ct))
            {
                yield return token;
            }
            yield break;
        }

        var tokenization = await TokenizeAsync(client, shards[0], req.Messages, ct);
        var tokenIds = tokenization.TokenIds;

        for (var generated = 0; generated < maxTokens; generated++)
        {
            ct.ThrowIfCancellationRequested();
            var nextToken = await ForwardAndSampleAsync(
                client, shards, tokenIds, temperature, topP, ct);

            if (nextToken == tokenization.EosTokenId)
                yield break;

            tokenIds.Add(nextToken);
            var text = await DecodeAsync(client, shards[^1], nextToken, ct);
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var shards = NormalizedShards();
        if (shards.Length == 0 || texts.Count == 0)
            return Array.Empty<float[]>();
        if (shards.Length > 1)
            throw new NotSupportedException("Embeddings require a full-model worker; pipeline shards only support chat generation.");

        var client = _http.CreateClient();
        AddAuthentication(client);
        client.Timeout = TimeSpan.FromMinutes(2);
        var payload = new { model = _opts.Model, input = texts };
        using var response = await client.PostAsJsonAsync($"{shards[0]}/v1/embeddings", payload, J, ct);
        await EnsureSuccessAsync(response, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("data").EnumerateArray()
            .OrderBy(element => element.GetProperty("index").GetInt32())
            .Select(element => element.GetProperty("embedding").EnumerateArray()
                .Select(value => value.GetSingle()).ToArray())
            .ToList();
    }

    private string[] NormalizedShards() => _opts.ShardEndpoints
        .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint))
        .Select(endpoint => endpoint.TrimEnd('/'))
        .ToArray();

    private void AddAuthentication(HttpClient client)
    {
        if (!string.IsNullOrWhiteSpace(_opts.ApiToken))
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                ClusterApiAuthenticationMiddleware.HeaderName, _opts.ApiToken);
    }

    private static async Task<TokenizationResult> TokenizeAsync(
        HttpClient client,
        string firstShard,
        IReadOnlyList<ClusterMessageDto> messages,
        CancellationToken ct)
    {
        var payload = new
        {
            messages = messages.Select(message => new { role = message.Role, content = message.Content }).ToList(),
        };
        using var response = await client.PostAsJsonAsync($"{firstShard}/v1/tokenize", payload, J, ct);
        await EnsureSuccessAsync(response, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var tokenIds = doc.RootElement.GetProperty("token_ids").EnumerateArray()
            .Select(token => token.GetInt32()).ToList();
        if (tokenIds.Count == 0)
            throw new InvalidOperationException("The first shard returned an empty prompt.");

        return new TokenizationResult(
            tokenIds,
            doc.RootElement.GetProperty("eos_token_id").GetInt32());
    }

    private static async Task<int> ForwardAndSampleAsync(
        HttpClient client,
        IReadOnlyList<string> shards,
        IReadOnlyList<int> tokenIds,
        float temperature,
        float topP,
        CancellationToken ct)
    {
        JsonElement? hiddenStates = null;

        for (var index = 0; index < shards.Count; index++)
        {
            object payload = index == 0
                ? new { token_ids = tokenIds, generate = false }
                : new { hidden_states = hiddenStates, generate = false };

            using var response = await client.PostAsJsonAsync($"{shards[index]}/v1/forward", payload, J, ct);
            await EnsureSuccessAsync(response, ct);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            if (index == shards.Count - 1)
                return SampleLastToken(doc.RootElement.GetProperty("logits"), temperature, topP);

            hiddenStates = doc.RootElement.GetProperty("hidden_states").Clone();
        }

        throw new InvalidOperationException("The shard pipeline did not produce logits.");
    }

    internal static int SampleLastToken(JsonElement logits, float temperature, float topP, Random? random = null)
    {
        var batches = logits.EnumerateArray();
        if (!batches.MoveNext())
            throw new InvalidOperationException("The last shard returned no logits batch.");

        var positions = batches.Current.EnumerateArray();
        JsonElement last = default;
        var found = false;
        while (positions.MoveNext())
        {
            last = positions.Current;
            found = true;
        }
        if (!found)
            throw new InvalidOperationException("The last shard returned no logits positions.");

        var values = last.EnumerateArray().Select((value, index) =>
            new TokenLogit(index, value.GetDouble())).ToArray();
        if (values.Length == 0)
            throw new InvalidOperationException("The last shard returned an empty vocabulary.");

        if (temperature <= 0)
            return values.MaxBy(value => value.Logit)!.TokenId;

        Array.Sort(values, static (left, right) => right.Logit.CompareTo(left.Logit));
        var max = values[0].Logit / temperature;
        var weights = new double[values.Length];
        var total = 0d;
        for (var i = 0; i < values.Length; i++)
        {
            weights[i] = Math.Exp((values[i].Logit / temperature) - max);
            total += weights[i];
        }

        var limit = Math.Clamp(topP, 0.0001f, 1f);
        var kept = values.Length;
        if (limit < 1f)
        {
            var cumulative = 0d;
            for (var i = 0; i < values.Length; i++)
            {
                cumulative += weights[i] / total;
                if (cumulative >= limit)
                {
                    kept = i + 1;
                    break;
                }
            }
        }

        var keptTotal = 0d;
        for (var i = 0; i < kept; i++)
            keptTotal += weights[i];

        var target = (random ?? Random.Shared).NextDouble() * keptTotal;
        var running = 0d;
        for (var i = 0; i < kept; i++)
        {
            running += weights[i];
            if (running >= target)
                return values[i].TokenId;
        }
        return values[kept - 1].TokenId;
    }

    private static async Task<string> DecodeAsync(
        HttpClient client,
        string shard,
        int tokenId,
        CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(
            $"{shard}/v1/decode", new { token_id = tokenId }, J, ct);
        await EnsureSuccessAsync(response, ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
    }

    private async IAsyncEnumerable<string> StreamSingleShard(
        HttpClient client,
        string url,
        ClusterChatRequest req,
        float temperature,
        float topP,
        int maxTokens,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var payload = new
        {
            model = _opts.Model,
            messages = req.Messages.Select(message => new { role = message.Role, content = message.Content }).ToList(),
            stream = true,
            temperature,
            top_p = topP,
            max_tokens = maxTokens,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/v1/chat/stream")
        {
            Content = JsonContent.Create(payload, options: J),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                yield break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line[6..];
            if (data == "[DONE]")
                yield break;
            if (string.IsNullOrWhiteSpace(data))
                continue;

            using var doc = JsonDocument.Parse(data);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;
            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var finishReason)
                && finishReason.ValueKind == JsonValueKind.String
                && finishReason.GetString() is not null)
            {
                yield break;
            }
            if (choice.TryGetProperty("delta", out var delta)
                && delta.TryGetProperty("content", out var content)
                && content.GetString() is { Length: > 0 } text)
            {
                yield return text;
            }
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        if (body.Length > 1000)
            body = body[..1000];
        throw new HttpRequestException(
            $"Shard returned {(int)response.StatusCode} ({response.ReasonPhrase}): {body}",
            inner: null,
            response.StatusCode);
    }

    private sealed record TokenizationResult(List<int> TokenIds, int EosTokenId);
    private sealed record TokenLogit(int TokenId, double Logit);
}
