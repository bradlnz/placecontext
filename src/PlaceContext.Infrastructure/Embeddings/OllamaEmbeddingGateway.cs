using System.Net.Http.Json;
using System.Text.Json;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.Configuration;

namespace PlaceContext.Infrastructure.Embeddings;

/// <summary>
/// Local <see cref="IEmbeddingGateway"/> backed by an Ollama embedding model (e.g. nomic-embed-text)
/// served in-cluster. Selected when <c>PlaceContext:Embeddings:Provider = "ollama"</c>. Lets run
/// summaries be vectorized into the pgvector store — so runs are semantically searchable and linkable in
/// the dependency graph — without sending data to a third-party API. Mirrors <see cref="OllamaLlmGateway"/>.
/// </summary>
public sealed class OllamaEmbeddingGateway : IEmbeddingGateway
{
    private readonly IHttpClientFactory _http;
    private readonly string _endpoint;
    private readonly string _model;

    public OllamaEmbeddingGateway(IHttpClientFactory http, IConfiguration config)
    {
        _http = http;
        var s = config.GetSection("PlaceContext:Embeddings:Ollama");
        // Fall back to the LLM Ollama endpoint so a single Ollama service serves both.
        _endpoint = (s["Endpoint"] ?? config["PlaceContext:Llm:Ollama:Endpoint"] ?? "http://localhost:11434").TrimEnd('/');
        _model = s["Model"] ?? "nomic-embed-text";
        Dimensions = int.TryParse(s["Dimensions"], out var d) && d > 0 ? d : 768; // nomic-embed-text = 768
    }

    public bool IsEnabled => true;
    public int Dimensions { get; }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return Array.Empty<float[]>();

        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(2);
        var vectors = new List<float[]>(texts.Count);
        foreach (var text in texts)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/api/embeddings");
            req.Content = JsonContent.Create(new { model = _model, prompt = text });
            using var resp = await client.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            // Ollama /api/embeddings: { "embedding": [floats...] }
            var vec = doc.RootElement.GetProperty("embedding").EnumerateArray().Select(v => (float)v.GetDouble()).ToArray();
            vectors.Add(vec);
        }
        return vectors;
    }
}
