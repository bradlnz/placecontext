using System.Net.Http.Json;
using System.Text.Json;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PlaceContext.Infrastructure.Embeddings;

/// <summary>
/// Cluster-backed <see cref="IEmbeddingGateway"/>: embeds via the ClusterHost sidecar
/// (<c>{endpoint}/embeddings</c>), which proxies to the shard server's <c>/v1/embeddings</c>.
/// The vectors come from the chat model itself (mean-pooled hidden states) — fully self-hosted,
/// no external API key. Registered when no Voyage key is configured but the cluster endpoint is.
/// Vectors land in the same stores (pgvector run/content embeddings, Qdrant chat memory) as any
/// other gateway — Qdrant/pgvector store and search, this gateway generates.
/// </summary>
public sealed class ClusterEmbeddingGateway : IEmbeddingGateway
{
    /// <summary>Per-text cap so embedding prompts stay well inside the chat model's context window.</summary>
    private const int MaxTextChars = 4000;

    private readonly IHttpClientFactory _http;
    private readonly string _endpoint;
    private readonly ILogger<ClusterEmbeddingGateway> _log;

    public ClusterEmbeddingGateway(IHttpClientFactory http, IConfiguration config, ILogger<ClusterEmbeddingGateway> log)
    {
        _http = http;
        _log = log;
        var section = config.GetSection("PlaceContext:ClusterChat");
        _endpoint = (section["Endpoint"] ?? "").TrimEnd('/');
        // Must match the shard model's hidden size (Qwen3-4B: 2560). The pgvector tables are
        // created with vector(Dimensions), so a wrong value fails inserts — the gateway logs a
        // warning whenever the cluster reports a different size.
        Dimensions = int.TryParse(section["EmbeddingDimensions"], out var d) && d > 0 ? d : 2560;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_endpoint);
    public int Dimensions { get; }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (!IsEnabled || texts.Count == 0) return Array.Empty<float[]>();

        var input = texts.Select(t => t.Length > MaxTextChars ? t[..MaxTextChars] : t).ToList();
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(2);

        using var resp = await client.PostAsJsonAsync($"{_endpoint}/embeddings", new { input }, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var vectors = root.GetProperty("vectors").EnumerateArray()
            .Select(v => v.EnumerateArray().Select(f => (float)f.GetDouble()).ToArray())
            .ToList();

        if (root.TryGetProperty("dimensions", out var dims) && dims.GetInt32() != Dimensions)
            _log.LogWarning("Cluster embeddings are {Actual}-dimensional but PlaceContext:ClusterChat:EmbeddingDimensions is {Configured} — pgvector inserts will fail until they match.",
                dims.GetInt32(), Dimensions);

        return vectors;
    }
}
