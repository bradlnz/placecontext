using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PlaceContext.Host.Controllers;

public sealed class ClusterPipeline
{
    private readonly IHttpClientFactory _http;
    private readonly ClusterProxyOptions _opts;
    private readonly ILogger<ClusterPipeline> _log;
    private static readonly JsonSerializerOptions J = new() { PropertyNameCaseInsensitive = true };
    private const string BOS = "<|im_start|>";
    private const string EOS = "<|im_end|>";

    public ClusterPipeline(
        IHttpClientFactory http,
        Microsoft.Extensions.Options.IOptions<ClusterProxyOptions> opts,
        ILogger<ClusterPipeline> log)
    {
        _http = http;
        _opts = opts.Value;
        _log = log;
    }

    public bool IsEnabled => _opts.ShardEndpoints.Count > 0;

    public async Task<string> GenerateAsync(ClusterChatRequest req, CancellationToken ct)
    {
        var sb = new StringBuilder();
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

        var prompt = BuildPrompt(req);
        var temp = req.Temperature ?? 0.7f;
        var topP = req.TopP ?? 0.9f;
        var maxTokens = req.MaxTokens ?? 2048;

        if (shards.Length == 1)
        {
            var r = await Fwd(client, shards[0], prompt, null, null, temp, topP, maxTokens, true, null, ct);
            if (r.TryGetProperty("generated_text", out var gt))
            {
                var t = gt.GetString();
                if (!string.IsNullOrEmpty(t)) yield return t;
            }
            yield break;
        }

        var init = await Fwd(client, shards[0], prompt, null, null, temp, topP, maxTokens, false, null, ct);
        var hs = init.GetProperty("hidden_states");
        var seqLen = hs[0][0].GetArrayLength();
        var mask = Enumerable.Repeat(1, seqLen).ToArray();
        var ids = new List<int>();

        for (var t = 0; t < maxTokens; t++)
        {
            ct.ThrowIfCancellationRequested();
            JsonElement logits = default;
            var cur = hs;

            for (var s = 1; s < shards.Length; s++)
            {
                var last = s == shards.Length - 1;
                var body = new { hidden_states = cur, attention_mask = mask, temperature = temp, top_p = topP, max_tokens = maxTokens, is_last = last, token_ids = ids.Count > 0 ? ids : null };
                var resp = await Raw(client, shards[s], body, ct);
                if (last) logits = resp.TryGetProperty("logits", out var l) ? l : default;
                else cur = resp.TryGetProperty("hidden_states", out var n) ? n : cur;
            }

            if (logits.ValueKind == JsonValueKind.Undefined) break;
            var next = Sample(logits, temp, topP);
            if (next == -1) break;
            ids.Add(next);

            var txt = await Decode(client, shards[0], next, ct);
            if (!string.IsNullOrEmpty(txt)) yield return txt;

            var emb = await Raw(client, shards[0], new { token_ids = ids, attention_mask = mask }, ct);
            hs = emb.TryGetProperty("hidden_states", out var n2) ? n2 : hs;
        }
    }

    private static string BuildPrompt(ClusterChatRequest req)
    {
        var sb = new StringBuilder();
        foreach (var m in req.Messages)
            sb.Append(BOS).Append(m.Role).Append('\n').Append(m.Content).Append(EOS).Append('\n');
        sb.Append(BOS).Append("assistant").Append('\n');
        return sb.ToString();
    }

    private async Task<JsonElement> Fwd(HttpClient c, string url, string? prompt, JsonElement? hs,
        int[]? mask, float temp, float topP, int maxTok, bool gen, List<int>? ids, CancellationToken ct)
    {
        var body = new { prompt, hidden_states = hs, attention_mask = mask,
            temperature = temp, top_p = topP, max_tokens = maxTok, generate = gen, token_ids = ids };
        return await Raw(c, url, body, ct);
    }

    private static async Task<JsonElement> Raw(HttpClient c, string url, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{url}/v1/forward")
        { Content = JsonContent.Create(body, options: J) };
        using var resp = await c.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>(J, ct);
    }

    private static async Task<string> Decode(HttpClient c, string url, int id, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{url}/v1/decode")
            { Content = JsonContent.Create(new { token_id = id }, options: J) };
            using var resp = await c.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return "";
            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(J, ct);
            return doc.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        }
        catch { return ""; }
    }

    private static int Sample(JsonElement logits, float temp, float topP)
    {
        var arr = logits[0][logits[0].GetArrayLength() - 1];
        var n = arr.GetArrayLength();
        var probs = new float[n];
        float maxVal = float.MinValue, sum = 0;
        for (var i = 0; i < n; i++) { var v = arr[i].GetSingle(); if (v > maxVal) maxVal = v; }
        for (var i = 0; i < n; i++) { probs[i] = MathF.Exp((arr[i].GetSingle() - maxVal) / MathF.Max(temp, 1e-6f)); sum += probs[i]; }
        for (var i = 0; i < n; i++) probs[i] /= sum;

        if (topP < 1.0f)
        {
            var indexed = probs.Select((p, i) => (p, i)).OrderByDescending(x => x.p).ToList();
            float cumul = 0; var cutoff = new HashSet<int>();
            foreach (var (p, i) in indexed) { cumul += p; cutoff.Add(i); if (cumul >= topP) break; }
            float rsum = 0; for (var i = 0; i < n; i++) if (!cutoff.Contains(i)) { rsum += probs[i]; probs[i] = 0; }
            for (var i = 0; i < n; i++) if (cutoff.Contains(i)) probs[i] /= (1.0f - rsum);
        }

        var rng = Random.Shared;
        var r = (float)rng.NextDouble();
        float cdf = 0;
        for (var i = 0; i < n; i++) { cdf += probs[i]; if (r <= cdf) return i; }
        return n - 1;
    }
}
