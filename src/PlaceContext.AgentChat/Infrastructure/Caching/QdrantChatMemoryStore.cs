using System.Net.Http.Json;
using PlaceContext.Application.Ports;
using System.Text.Json;

namespace PlaceContext.AgentChat.Infrastructure.Caching;

/// <summary>
/// Qdrant-backed chat memory store with semantic search. Stores conversation embeddings
/// in Qdrant for semantic retrieval across sessions. Falls back to Redis for session metadata.
/// </summary>
public sealed class QdrantChatMemoryStore : IChatMemoryStore
{
    private readonly IChatMemoryStore _fallback;
    private readonly IEmbeddingGateway _embeddings;
    private readonly HttpClient _http;
    private readonly string _qdrantUrl;
    private readonly string _collectionName;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public QdrantChatMemoryStore(IChatMemoryStore fallback, IEmbeddingGateway embeddings, HttpClient http, string qdrantUrl, string collectionName)
    {
        _fallback = fallback;
        _embeddings = embeddings;
        _http = http;
        _qdrantUrl = qdrantUrl.TrimEnd('/');
        _collectionName = collectionName;
    }

    public async Task<IReadOnlyList<ChatSessionSummary>> ListSessionsAsync(Guid projectId, CancellationToken ct = default)
        => await _fallback.ListSessionsAsync(projectId, ct);

    public async Task<ChatSessionMemory?> GetSessionAsync(Guid sessionId, CancellationToken ct = default)
        => await _fallback.GetSessionAsync(sessionId, ct);

    public async Task SaveSessionAsync(Guid sessionId, ChatSessionMemory memory, CancellationToken ct = default)
    {
        await _fallback.SaveSessionAsync(sessionId, memory, ct);

        // Embed new messages and upsert to Qdrant
        if (_embeddings.IsEnabled)
        {
            var newMessages = memory.Messages.Skip(Math.Max(0, memory.Messages.Count - 5)).ToList();
            if (newMessages.Count > 0)
            {
                try
                {
                    var vectors = await _embeddings.EmbedAsync(newMessages.Select(m => m.Content).ToList(), ct);
                    var points = new List<object>();
                    for (var i = 0; i < newMessages.Count; i++)
                    {
                        points.Add(new
                        {
                            id = $"{sessionId:N}-{i}",
                            vector = vectors[i],
                            payload = new
                            {
                                session_id = sessionId.ToString("N"),
                                project_id = memory.ProjectId.ToString("N"),
                                role = newMessages[i].Role,
                                content = newMessages[i].Content,
                                timestamp = newMessages[i].Timestamp.ToString("O"),
                            }
                        });
                    }
                    await UpsertPointsAsync(points, ct);
                }
                catch { /* best-effort embedding */ }
            }
        }
    }

    public async Task DeleteSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        await _fallback.DeleteSessionAsync(sessionId, ct);
        // Delete points from Qdrant
        try
        {
            await _http.PostAsync($"{_qdrantUrl}/collections/{_collectionName}/points/delete",
                JsonContent.Create(new { filter = new { must = new[] { new { key = "session_id", match = new { value = sessionId.ToString("N") } } } } }), ct);
        }
        catch { }
    }

    public async Task ClearSessionMemoryAsync(Guid sessionId, CancellationToken ct = default)
        => await _fallback.ClearSessionMemoryAsync(sessionId, ct);

    /// <summary>Semantic search across all project conversations.</summary>
    public async Task<IReadOnlyList<ChatMemoryMatch>> SearchAsync(Guid projectId, string query, int limit = 5, CancellationToken ct = default)
    {
        if (!_embeddings.IsEnabled) return Array.Empty<ChatMemoryMatch>();

        var vectors = await _embeddings.EmbedAsync(new[] { query }, ct);
        if (vectors.Count == 0) return Array.Empty<ChatMemoryMatch>();

        var searchBody = new
        {
            vector = vectors[0],
            limit,
            filter = new { must = new[] { new { key = "project_id", match = new { value = projectId.ToString("N") } } } },
            with_payload = true,
        };

        var resp = await _http.PostAsync($"{_qdrantUrl}/collections/{_collectionName}/points/search",
            JsonContent.Create(searchBody, options: Json), ct);
        if (!resp.IsSuccessStatusCode) return Array.Empty<ChatMemoryMatch>();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        var matches = new List<ChatMemoryMatch>();
        if (result.TryGetProperty("result", out var results))
        {
            foreach (var point in results.EnumerateArray())
            {
                var payload = point.GetProperty("payload");
                matches.Add(new ChatMemoryMatch(
                    payload.GetProperty("content").GetString() ?? "",
                    payload.GetProperty("role").GetString() ?? "",
                    Guid.Parse(payload.GetProperty("session_id").GetString()!),
                    point.TryGetProperty("score", out var score) ? score.GetSingle() : 0f));
            }
        }
        return matches;
    }

    private async Task UpsertPointsAsync(List<object> points, CancellationToken ct)
    {
        var body = new { points };
        await _http.PutAsync($"{_qdrantUrl}/collections/{_collectionName}/points",
            JsonContent.Create(body, options: Json), ct);
    }
}
