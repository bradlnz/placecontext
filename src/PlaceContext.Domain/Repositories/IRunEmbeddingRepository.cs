using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

/// <summary>
/// Persistence + similarity search for <see cref="RunEmbedding"/> vectors (project-scoped, tenant-filtered).
/// The production adapter is pgvector-backed; it self-initializes lazily and degrades gracefully when the
/// extension is unavailable.
/// </summary>
public interface IRunEmbeddingRepository
{
    /// <summary>Stores a run-output embedding. No-op (best-effort) if the vector store is unavailable.</summary>
    Task AddAsync(RunEmbedding embedding, CancellationToken ct = default);

    /// <summary>
    /// Returns the <paramref name="take"/> nearest run-output embeddings to <paramref name="queryVector"/>
    /// within a project, by cosine similarity (highest first). Empty if the store is unavailable.
    /// </summary>
    Task<IReadOnlyList<RunEmbeddingMatch>> SearchAsync(
        Guid projectId, float[] queryVector, int take, CancellationToken ct = default);
}
