namespace PlaceContext.Domain.Entities;

/// <summary>A run-embedding search hit with its similarity score (higher = more similar, 0..1 cosine).</summary>
public sealed record RunEmbeddingMatch(RunEmbedding Embedding, double Score);
