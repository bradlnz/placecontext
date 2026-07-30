using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.VectorStore;

public sealed class QdrantContentIndexer : IContentIndexer
{
    private static int _collectionState;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public const int MaxTextChars = 8000;
    private static readonly Guid Namespace = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8"); // DNS namespace

    private readonly IEmbeddingGateway _gateway;
    private readonly ICurrentTenant _tenant;
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _collection;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public QdrantContentIndexer(
        IEmbeddingGateway gateway,
        ICurrentTenant tenant,
        IHttpClientFactory httpFactory,
        string qdrantUrl)
    {
        _gateway = gateway;
        _tenant = tenant;
        _http = httpFactory.CreateClient();
        _baseUrl = qdrantUrl.TrimEnd('/');
        _collection = "content_embeddings";
    }

    public bool IsEnabled => _gateway.IsEnabled && _gateway.Dimensions > 0;

    public async Task IndexAsync(Guid projectId, string kind, string sourceKey, string text, CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(sourceKey)) return;
        await IndexManyAsync(projectId, kind, new[] { (sourceKey, text) }, ct);
    }

    public async Task IndexManyAsync(
        Guid projectId, string kind, IReadOnlyList<(string SourceKey, string Text)> items, CancellationToken ct = default)
    {
        if (!IsEnabled || items.Count == 0) return;
        if (!await EnsureCollectionAsync(_gateway.Dimensions, ct)) return;

        var prepared = items
            .Where(i => !string.IsNullOrWhiteSpace(i.SourceKey) && !string.IsNullOrWhiteSpace(i.Text))
            .Select(i => (
                Key: i.SourceKey.Trim(),
                Text: i.Text.Length > MaxTextChars ? i.Text[..MaxTextChars] : i.Text.Trim()))
            .Where(i => i.Text.Length > 0)
            .GroupBy(i => i.Key, StringComparer.Ordinal)
            .Select(g => g.Last())
            .ToList();
        if (prepared.Count == 0) return;

        try
        {
            const int batch = 32;
            for (var offset = 0; offset < prepared.Count; offset += batch)
            {
                var slice = prepared.Skip(offset).Take(batch).ToList();
                var vectors = await _gateway.EmbedAsync(slice.Select(s => s.Text).ToList(), ct);
                if (vectors.Count != slice.Count) continue;

                var points = new List<object>();
                for (var i = 0; i < slice.Count; i++)
                {
                    if (vectors[i].Length == 0) continue;
                    var pointId = DeterministicId(_tenant.TenantId, projectId, kind, slice[i].Key);
                    points.Add(new
                    {
                        id = pointId,
                        vector = vectors[i],
                        payload = new
                        {
                            tenant_id = _tenant.TenantId.ToString("N"),
                            project_id = projectId.ToString("N"),
                            kind,
                            source_key = slice[i].Key,
                            text = slice[i].Text,
                            created_at = DateTimeOffset.UtcNow.ToString("O"),
                        }
                    });
                }

                if (points.Count > 0)
                {
                    await _http.PutAsync($"{_baseUrl}/collections/{_collection}/points",
                        JsonContent.Create(new { points }, options: Json), ct);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[QdrantContentIndexer] Index failed for project {projectId} kind {kind} ({items.Count} items): {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<ContentSearchHit>> SearchAsync(
        Guid projectId, string query, int take = 10, string? kind = null, CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(query))
            return Array.Empty<ContentSearchHit>();

        take = Math.Clamp(take, 1, 50);
        try
        {
            var vectors = await _gateway.EmbedAsync(new[] { query.Length > MaxTextChars ? query[..MaxTextChars] : query }, ct);
            if (vectors.Count == 0 || vectors[0].Length == 0) return Array.Empty<ContentSearchHit>();

            var must = new List<object>
            {
                new { key = "tenant_id", match = new { value = _tenant.TenantId.ToString("N") } },
                new { key = "project_id", match = new { value = projectId.ToString("N") } }
            };
            if (kind is not null)
                must.Add(new { key = "kind", match = new { value = kind } });

            var searchBody = new
            {
                vector = vectors[0],
                limit = take,
                filter = new { must },
                with_payload = true,
                with_vector = false,
            };

            var resp = await _http.PostAsync(
                $"{_baseUrl}/collections/{_collection}/points/search",
                JsonContent.Create(searchBody, options: Json), ct);
            if (!resp.IsSuccessStatusCode) return Array.Empty<ContentSearchHit>();

            var result = await resp.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
            var hits = new List<ContentSearchHit>();
            if (result.TryGetProperty("result", out var results))
            {
                foreach (var point in results.EnumerateArray())
                {
                    var p = point.GetProperty("payload");
                    var createdAt = DateTimeOffset.Parse(p.GetProperty("created_at").GetString()!, CultureInfo.InvariantCulture);
                    hits.Add(new ContentSearchHit(
                        p.GetProperty("kind").GetString()!,
                        p.GetProperty("source_key").GetString()!,
                        p.GetProperty("text").GetString() ?? "",
                        point.TryGetProperty("score", out var s) ? s.GetDouble() : 0.0,
                        createdAt));
                }
            }
            return hits;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[QdrantContentIndexer] Search failed for project {projectId}: {ex.Message}");
            return Array.Empty<ContentSearchHit>();
        }
    }

    private async Task<bool> EnsureCollectionAsync(int dims, CancellationToken ct)
    {
        if (_collectionState == 1) return true;
        if (_collectionState == 2) return false;

        await Gate.WaitAsync(ct);
        try
        {
            if (_collectionState == 1) return true;
            if (_collectionState == 2) return false;

            var createBody = new
            {
                vectors = new { size = dims, distance = "Cosine" }
            };
            var resp = await _http.PutAsync($"{_baseUrl}/collections/{_collection}",
                JsonContent.Create(createBody, options: Json), ct);
            _collectionState = resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.Conflict ? 1 : 2;
            return _collectionState == 1;
        }
        catch
        {
            _collectionState = 2;
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string DeterministicId(Guid tenantId, Guid projectId, string kind, string sourceKey)
    {
        var raw = $"{tenantId:N}:{projectId:N}:{kind}:{sourceKey}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var guid = new Guid(bytes[..16]);
        return guid.ToString("N");
    }
}
