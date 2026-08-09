namespace PlaceContext.Application.Ports;

/// <summary>
/// Shared content-indexing boundary for project-owned searchable text.
/// and store the vector for cosine search. Best-effort — never throws when embeddings are disabled.
/// </summary>
public interface IContentIndexer
{
    /// <summary>True when a real embedding backend is configured.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Upsert one chunk identified by <paramref name="sourceKey"/> within <paramref name="kind"/>.
    /// Empty text is a no-op. Truncates oversized text before embedding.
    /// </summary>
    Task IndexAsync(Guid projectId, string kind, string sourceKey, string text, CancellationToken ct = default);

    /// <summary>Embed and store many chunks (batched gateway call when possible).</summary>
    Task IndexManyAsync(Guid projectId, string kind, IReadOnlyList<(string SourceKey, string Text)> items, CancellationToken ct = default);

    /// <summary>Semantic search over all indexed kinds for a project (or filter by kind).</summary>
    Task<IReadOnlyList<ContentSearchHit>> SearchAsync(
        Guid projectId, string query, int take = 10, string? kind = null, CancellationToken ct = default);
}
