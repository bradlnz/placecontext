using Microsoft.Extensions.Caching.Distributed;
using PlaceContext.Application.Ports;

namespace PlaceContext.AgentChat.Infrastructure.Slack;

/// <summary>Redis/IDistributedCache-backed Slack thread → chat session map + event-id dedupe.</summary>
public sealed class DistributedCacheSlackThreadSessionStore : ISlackThreadSessionStore
{
    private static readonly DistributedCacheEntryOptions ThreadOpts = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30),
    };
    private static readonly DistributedCacheEntryOptions EventOpts = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15),
    };

    private readonly IDistributedCache _cache;

    public DistributedCacheSlackThreadSessionStore(IDistributedCache cache) => _cache = cache;

    public async Task<Guid> GetOrCreateSessionIdAsync(string teamId, string channelId, string threadRootTs, CancellationToken ct = default)
    {
        var key = ThreadKey(teamId, channelId, threadRootTs);
        var existing = await _cache.GetStringAsync(key, ct);
        if (!string.IsNullOrEmpty(existing) && Guid.TryParse(existing, out var id) && id != Guid.Empty)
            return id;

        var sessionId = Guid.NewGuid();
        await _cache.SetStringAsync(key, sessionId.ToString("N"), ThreadOpts, ct);
        return sessionId;
    }

    public async Task<bool> TryClaimEventAsync(string eventId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return true;
        var key = EventKey(eventId);
        var existing = await _cache.GetStringAsync(key, ct);
        if (!string.IsNullOrEmpty(existing)) return false;
        await _cache.SetStringAsync(key, "1", EventOpts, ct);
        return true;
    }

    private static string ThreadKey(string teamId, string channelId, string threadRootTs)
        => $"pc:slack:thread:{teamId}:{channelId}:{threadRootTs}";

    private static string EventKey(string eventId) => $"pc:slack:event:{eventId}";
}
