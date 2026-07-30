using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.VectorStore;

public sealed class QdrantRunEmbeddingRepository : IRunEmbeddingRepository
{
    private static int _collectionState;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IEmbeddingGateway _embeddings;
    private readonly ICurrentTenant _tenant;
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _collection;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly float[] EmptyVector = [];

    public QdrantRunEmbeddingRepository(
        IEmbeddingGateway embeddings,
        ICurrentTenant tenant,
        IHttpClientFactory httpFactory,
        string qdrantUrl)
    {
        _embeddings = embeddings;
        _tenant = tenant;
        _http = httpFactory.CreateClient();
        _baseUrl = qdrantUrl.TrimEnd('/');
        _collection = "run_embeddings";
    }

    public async Task AddAsync(RunEmbedding embedding, CancellationToken ct = default)
    {
        if (!_embeddings.IsEnabled) return;
        if (!await EnsureCollectionAsync(_embeddings.Dimensions, ct)) return;

            var point = new
            {
                id = embedding.Id.ToString("N"),
                vector = embedding.Vector,
                payload = new
                {
                    tenant_id = _tenant.TenantId.ToString("N"),
                    job_run_id = embedding.JobRunId.ToString("N"),
                    job_id = embedding.JobId.ToString("N"),
                    project_id = embedding.ProjectId.ToString("N"),
                    text = embedding.Text,
                    created_at = embedding.CreatedAt.ToString("O"),
                }
            };
            await UpsertPointsAsync(new { points = new[] { point } }, ct);
    }

    public async Task<IReadOnlyList<RunEmbeddingMatch>> SearchAsync(
        Guid projectId, float[] queryVector, int take, CancellationToken ct = default)
    {
        if (queryVector.Length == 0 || !_embeddings.IsEnabled)
            return Array.Empty<RunEmbeddingMatch>();

        var searchBody = new
        {
            vector = queryVector,
            limit = take,
            filter = new
            {
                must = new List<object>
                {
                    new { key = "tenant_id", match = new { value = _tenant.TenantId.ToString("N") } },
                    new { key = "project_id", match = new { value = projectId.ToString("N") } }
                }
            },
            with_payload = true,
            with_vector = false,
        };

        var resp = await _http.PostAsync(
            $"{_baseUrl}/collections/{_collection}/points/search",
            JsonContent.Create(searchBody, options: Json), ct);
        if (!resp.IsSuccessStatusCode) return Array.Empty<RunEmbeddingMatch>();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        var matches = new List<RunEmbeddingMatch>();
        if (result.TryGetProperty("result", out var results))
        {
            foreach (var point in results.EnumerateArray())
            {
                var id = Guid.Parse(point.GetProperty("id").GetString()!);
                var p = point.GetProperty("payload");
                var jobRunId = Guid.Parse(p.GetProperty("job_run_id").GetString()!);
                var jobId = Guid.Parse(p.GetProperty("job_id").GetString()!);
                var projectIdFromPayload = Guid.Parse(p.GetProperty("project_id").GetString()!);
                var text = p.GetProperty("text").GetString() ?? "";
                var createdAt = DateTimeOffset.Parse(p.GetProperty("created_at").GetString()!, CultureInfo.InvariantCulture);

                var embedding = RunEmbedding.Rehydrate(id, jobRunId, jobId, projectIdFromPayload, text, EmptyVector, createdAt);
                var score = point.TryGetProperty("score", out var s) ? s.GetDouble() : 0.0;
                matches.Add(new RunEmbeddingMatch(embedding, score));
            }
        }
        return matches;
    }

    public async Task<IReadOnlyList<RunEmbedding>> ListForProjectAsync(
        Guid projectId, int take = 200, CancellationToken ct = default)
    {
        if (!_embeddings.IsEnabled) return Array.Empty<RunEmbedding>();

        var results = new List<RunEmbedding>();
        string? nextOffset = null;

        do
        {
            var scrollBody = new
            {
                limit = Math.Min(take - results.Count, 1000),
                filter = new
                {
                    must = new List<object>
                    {
                        new { key = "tenant_id", match = new { value = _tenant.TenantId.ToString("N") } },
                        new { key = "project_id", match = new { value = projectId.ToString("N") } }
                    }
                },
                with_payload = true,
                with_vector = true,
            };

            object body = nextOffset is null
                ? scrollBody
                : new { offset = nextOffset, limit = scrollBody.limit, filter = scrollBody.filter, with_payload = true, with_vector = true };
            var resp = await _http.PostAsync(
                $"{_baseUrl}/collections/{_collection}/points/scroll",
                JsonContent.Create(body, options: Json), ct);
            if (!resp.IsSuccessStatusCode) break;

            var result = await resp.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
            if (!result.TryGetProperty("result", out var r)) break;

            if (r.TryGetProperty("points", out var points))
            {
                foreach (var point in points.EnumerateArray())
                {
                    var id = Guid.Parse(point.GetProperty("id").GetString()!);
                    var p = point.GetProperty("payload");
                    var jobRunId = Guid.Parse(p.GetProperty("job_run_id").GetString()!);
                    var jobId = Guid.Parse(p.GetProperty("job_id").GetString()!);
                    var projectIdFromPayload = Guid.Parse(p.GetProperty("project_id").GetString()!);
                    var text = p.GetProperty("text").GetString() ?? "";
                    var createdAt = DateTimeOffset.Parse(p.GetProperty("created_at").GetString()!, CultureInfo.InvariantCulture);
                    var vector = point.GetProperty("vector").EnumerateArray().Select(v => (float)v.GetDouble()).ToArray();

                    results.Add(RunEmbedding.Rehydrate(id, jobRunId, jobId, projectIdFromPayload, text, vector, createdAt));
                }
            }

            nextOffset = r.TryGetProperty("next_page_offset", out var n) ? n.GetRawText() : null;
        }
        while (nextOffset is not null && results.Count < take);

        return results;
    }

    private async Task UpsertPointsAsync<T>(T points, CancellationToken ct)
    {
        try
        {
            await _http.PutAsync($"{_baseUrl}/collections/{_collection}/points",
                JsonContent.Create(new { points }, options: Json), ct);
        }
        catch { /* best-effort */ }
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
}
