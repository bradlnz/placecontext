using System.Collections.Concurrent;
using PlaceContext.Application.Ports;

namespace PlaceContext.AgentChat.Infrastructure.Slack;

/// <summary>In-process fallback when Redis/distributed cache is not configured.</summary>
public sealed class MemorySlackThreadSessionStore : ISlackThreadSessionStore
{
    private readonly ConcurrentDictionary<string, Guid> _threads = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _events = new(StringComparer.Ordinal);

    public Task<Guid> GetOrCreateSessionIdAsync(string teamId, string channelId, string threadRootTs, CancellationToken ct = default)
    {
        var key = $"{teamId}:{channelId}:{threadRootTs}";
        var id = _threads.GetOrAdd(key, _ => Guid.NewGuid());
        return Task.FromResult(id);
    }

    public Task<bool> TryClaimEventAsync(string eventId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return Task.FromResult(true);
        return Task.FromResult(_events.TryAdd(eventId, 0));
    }
}
