using System.Net.Http.Json;
using System.Text.Json;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.Configuration;

namespace PlaceContext.Infrastructure.Embeddings;

/// <summary>
/// Voyage AI-backed <see cref="IEmbeddingGateway"/>. Registered only when <c>PlaceContext:Voyage:ApiKey</c>
/// is set; otherwise the Null gateway is used. Mirrors <see cref="Llm.AnthropicLlmGateway"/> (an
/// <see cref="IHttpClientFactory"/> + config). Model and dimensions are configurable.
/// </summary>
public sealed class VoyageEmbeddingGateway : IEmbeddingGateway
{
    private const string Endpoint = "https://api.voyageai.com/v1/embeddings";

    private readonly IHttpClientFactory _http;
    private readonly string _apiKey;
    private readonly string _model;

    public VoyageEmbeddingGateway(IHttpClientFactory http, IConfiguration config)
    {
        _http = http;
        var voyage = config.GetSection("PlaceContext:Voyage");
        _apiKey = voyage["ApiKey"] ?? "";
        _model = voyage["Model"] ?? "voyage-3";
        Dimensions = int.TryParse(voyage["Dimensions"], out var d) ? d : 1024;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_apiKey);
    public int Dimensions { get; }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return Array.Empty<float[]>();

        var client = _http.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        req.Headers.Add("Authorization", $"Bearer {_apiKey}");
        req.Content = JsonContent.Create(new
        {
            input = texts,
            model = _model,
            input_type = "document",
        });

        using var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        // { data: [ { embedding: [..], index }, ... ] } — order by index to match the inputs.
        return doc.RootElement.GetProperty("data").EnumerateArray()
            .OrderBy(e => e.GetProperty("index").GetInt32())
            .Select(e => e.GetProperty("embedding").EnumerateArray().Select(v => (float)v.GetDouble()).ToArray())
            .ToList();
    }
}
