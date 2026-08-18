using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

/// <summary>In-memory <see cref="IContentIndexer"/> for unit tests.</summary>
public sealed class FakeContentIndexer : IContentIndexer
{
    public bool IsEnabled { get; set; } = true;
    public List<(Guid ProjectId, string Kind, string SourceKey, string Text)> Indexed { get; } = new();
    public IReadOnlyList<ContentSearchHit> HitsToReturn { get; set; } = Array.Empty<ContentSearchHit>();
    public string? LastSearchKind { get; private set; }

    public Task IndexAsync(Guid projectId, string kind, string sourceKey, string text, CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        Indexed.Add((projectId, kind, sourceKey, text));
        return Task.CompletedTask;
    }

    public Task IndexManyAsync(Guid projectId, string kind, IReadOnlyList<(string SourceKey, string Text)> items, CancellationToken ct = default)
    {
        if (!IsEnabled) return Task.CompletedTask;
        foreach (var (key, text) in items)
            if (!string.IsNullOrWhiteSpace(text))
                Indexed.Add((projectId, kind, key, text));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ContentSearchHit>> SearchAsync(
        Guid projectId, string query, int take = 10, string? kind = null, CancellationToken ct = default)
    {
        LastSearchKind = kind;
        return Task.FromResult(HitsToReturn);
    }
}
