namespace PlaceContext.Application.Ports;

/// <summary>
/// Stable kind strings for universal RAG content. Every kind is embeddable and its source text
/// is encrypted at rest; vectors live in pgvector for cosine search.
/// </summary>
public static class ContentKind
{
    public const string RunOutput = "run_output";
    public const string ProjectData = "project_data";
    public const string Context = "context";
    public const string Decision = "decision";
    public const string Activity = "activity";
    public const string Requirements = "requirements";
    public const string Event = "event";
    public const string Chart = "chart";
}

/// <summary>One semantic search hit over indexed project content.</summary>
public sealed record ContentSearchHit(
    string Kind,
    string SourceKey,
    string Text,
    double Score,
    DateTimeOffset CreatedAt);

/// <summary>
/// Universal RAG indexer: embed any text chunk for a project, encrypt the source text at rest,
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
