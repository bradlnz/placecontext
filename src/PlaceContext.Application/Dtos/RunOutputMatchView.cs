namespace PlaceContext.Application.Dtos;

/// <summary>Read model: a semantically-similar job-run output hit from the embedding store.</summary>
public sealed record RunOutputMatchView(
    Guid JobRunId,
    Guid JobId,
    string Text,
    /// <summary>Cosine similarity to the query (0..1, higher = more similar).</summary>
    double Score,
    DateTimeOffset CreatedAt);
