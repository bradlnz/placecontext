using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace PlaceContext.AgentChat.Infrastructure.Caching;

/// <summary>
/// Redis-backed chat memory store. Stores session lists and full conversation memory
/// with 30-day TTL. Sessions are keyed by project for multi-tenant isolation.
/// </summary>
public sealed class RedisChatMemoryStore : IChatMemoryStore
{
    private readonly IDistributedCache _cache;
    private static readonly DistributedCacheEntryOptions SessionOpts = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30),
    };

    public RedisChatMemoryStore(IDistributedCache cache) => _cache = cache;

    public async Task<IReadOnlyList<ChatSessionSummary>> ListSessionsAsync(Guid projectId, CancellationToken ct = default)
    {
        var json = await _cache.GetStringAsync(SessionsKey(projectId), ct);
        if (string.IsNullOrEmpty(json)) return Array.Empty<ChatSessionSummary>();

        var summaries = JsonSerializer.Deserialize<List<ChatSessionSummary>>(json);
        return (IReadOnlyList<ChatSessionSummary>)(summaries?.OrderByDescending(s => s.LastMessageAt).ToList() ?? new List<ChatSessionSummary>());
    }

    public async Task<ChatSessionMemory?> GetSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var json = await _cache.GetStringAsync(SessionKey(sessionId), ct);
        if (string.IsNullOrEmpty(json)) return null;
        return JsonSerializer.Deserialize<ChatSessionMemory>(json);
    }

    public async Task SaveSessionAsync(Guid sessionId, ChatSessionMemory memory, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(memory);
        await _cache.SetStringAsync(SessionKey(sessionId), json, SessionOpts, ct);

        // Update the session list for the project
        var summaries = await ListSessionsAsync(memory.ProjectId, ct);
        var existing = summaries.FirstOrDefault(s => s.Id == sessionId);
        var newSummary = new ChatSessionSummary(
            sessionId, memory.ProjectId, memory.Title,
            memory.Messages.Count, memory.CreatedAt, memory.LastMessageAt);

        var updated = summaries.Where(s => s.Id != sessionId).Append(newSummary).ToList();
        await _cache.SetStringAsync(SessionsKey(memory.ProjectId), JsonSerializer.Serialize(updated), SessionOpts, ct);
    }

    public async Task DeleteSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await GetSessionAsync(sessionId, ct);
        if (session != null)
        {
            var summaries = await ListSessionsAsync(session.ProjectId, ct);
            var updated = summaries.Where(s => s.Id != sessionId).ToList();
            await _cache.SetStringAsync(SessionsKey(session.ProjectId), JsonSerializer.Serialize(updated), SessionOpts, ct);
        }
        await _cache.RemoveAsync(SessionKey(sessionId), ct);
    }

    public async Task ClearSessionMemoryAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await GetSessionAsync(sessionId, ct);
        if (session != null)
        {
            var cleared = session with { Messages = new() };
            await SaveSessionAsync(sessionId, cleared, ct);
        }
    }

    private static string SessionsKey(Guid projectId) => $"pc:chat:sessions:{projectId:N}";
    private static string SessionKey(Guid sessionId) => $"pc:chat:session:{sessionId:N}";
}
