namespace PlaceContext.Domain.Entities;

/// <summary>
/// Entity: a vector embedding of a job run's organized output, linked back to the run/job/project. These
/// become semantically-searchable nodes in the dependency graph (related runs surface by similarity).
/// The text and vector are opaque to the domain.
/// </summary>
public sealed class RunEmbedding
{
    private RunEmbedding(
        Guid id, Guid jobRunId, Guid jobId, Guid projectId, string text, float[] vector, DateTimeOffset createdAt)
    {
        Id = id;
        JobRunId = jobRunId;
        JobId = jobId;
        ProjectId = projectId;
        Text = text;
        Vector = vector;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public Guid JobRunId { get; }
    public Guid JobId { get; }
    public Guid ProjectId { get; }

    /// <summary>The text that was embedded (the organized run output, possibly truncated).</summary>
    public string Text { get; }

    /// <summary>The embedding vector.</summary>
    public float[] Vector { get; }

    public DateTimeOffset CreatedAt { get; }

    public static RunEmbedding Create(
        Guid jobRunId, Guid jobId, Guid projectId, string text, float[] vector, DateTimeOffset createdAt)
    {
        if (jobRunId == Guid.Empty) throw new ArgumentException("JobRunId must not be empty.", nameof(jobRunId));
        if (vector is null || vector.Length == 0) throw new ArgumentException("Vector must not be empty.", nameof(vector));
        return new RunEmbedding(Guid.NewGuid(), jobRunId, jobId, projectId, text ?? "", vector, createdAt);
    }

    public static RunEmbedding Rehydrate(
        Guid id, Guid jobRunId, Guid jobId, Guid projectId, string text, float[] vector, DateTimeOffset createdAt)
        => new(id, jobRunId, jobId, projectId, text, vector, createdAt);
}

/// <summary>A run-embedding search hit with its similarity score (higher = more similar, 0..1 cosine).</summary>
public sealed record RunEmbeddingMatch(RunEmbedding Embedding, double Score);
