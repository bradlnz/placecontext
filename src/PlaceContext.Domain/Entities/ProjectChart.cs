namespace PlaceContext.Domain.Entities;

/// <summary>
/// One rendered analytics chart for a project table: the self-contained HTML document the local
/// LLM drew over the table's data. Generation is slow (minutes on CPU), so charts are produced by
/// a background sweep and stored here; the Analytics tab only ever reads. One chart per
/// project+table — a refresh replaces it.
/// </summary>
public sealed class ProjectChart
{
    private ProjectChart(Guid id, Guid projectId, string tableName, string html, DateTimeOffset generatedAt)
    {
        Id = id;
        ProjectId = projectId;
        TableName = tableName;
        Html = html;
        GeneratedAt = generatedAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string TableName { get; }

    /// <summary>The chart itself: a complete, self-contained HTML document (inline SVG, themed).</summary>
    public string Html { get; }

    public DateTimeOffset GeneratedAt { get; }

    public static ProjectChart Create(Guid projectId, string tableName, string html, DateTimeOffset generatedAt)
    {
        if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentException("Table name is required.", nameof(tableName));
        if (string.IsNullOrWhiteSpace(html)) throw new ArgumentException("Chart HTML is required.", nameof(html));
        return new(Guid.NewGuid(), projectId, tableName, html, generatedAt);
    }

    public static ProjectChart Rehydrate(Guid id, Guid projectId, string tableName, string html, DateTimeOffset generatedAt)
        => new(id, projectId, tableName, html, generatedAt);
}
