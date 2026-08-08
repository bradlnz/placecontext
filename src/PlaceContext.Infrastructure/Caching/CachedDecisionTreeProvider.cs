using Microsoft.Extensions.Caching.Memory;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Caching;

/// <summary>
/// Caching decorator over <see cref="IUncachedDecisionTreeProvider"/>. Assembling a project's tree replays
/// the full activity ledger, all decisions, the tool-call log, and an O(n²) all-pairs cosine pass
/// over every embedded run output — and the portal recomputed all of that on every project page
/// open. A short TTL keeps repeated opens (and the org-wide brain rollup, which builds every
/// project's tree) served from memory while staying fresh enough that new activity appears within
/// seconds. Project ids are GUIDs — unique across tenants — so the id alone is a safe cache key.
/// The explicit rebuild action calls <see cref="Invalidate"/> first and always reassembles.
/// </summary>
public sealed class CachedDecisionTreeProvider : IDecisionTreeProvider
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly IUncachedDecisionTreeProvider _inner;
    private readonly IMemoryCache _cache;

    public CachedDecisionTreeProvider(IUncachedDecisionTreeProvider inner, IMemoryCache cache)
        => (_inner, _cache) = (inner, cache);

    private static string Key(ProjectId projectId) => "dtree:" + projectId.Value;

    public async Task<DecisionTree> BuildAsync(ProjectId projectId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(Key(projectId), out DecisionTree? cached) && cached is not null)
            return cached;

        var tree = await _inner.BuildAsync(projectId, ct);
        _cache.Set(Key(projectId), tree, Ttl);
        return tree;
    }

    public void Invalidate(ProjectId projectId) => _cache.Remove(Key(projectId));
}
