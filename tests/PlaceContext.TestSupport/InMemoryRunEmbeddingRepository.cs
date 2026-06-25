using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.TestSupport;

/// <summary>In-memory <see cref="IRunEmbeddingRepository"/> with cosine-similarity search (stands in for
/// the pgvector store in unit tests).</summary>
public sealed class InMemoryRunEmbeddingRepository : IRunEmbeddingRepository
{
    public List<RunEmbedding> Store { get; } = new();

    public Task AddAsync(RunEmbedding embedding, CancellationToken ct = default)
    {
        Store.Add(embedding);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RunEmbeddingMatch>> SearchAsync(
        Guid projectId, float[] queryVector, int take, CancellationToken ct = default)
    {
        var matches = Store
            .Where(e => e.ProjectId == projectId)
            .Select(e => new RunEmbeddingMatch(e, Cosine(e.Vector, queryVector)))
            .OrderByDescending(m => m.Score)
            .Take(take)
            .ToList();
        return Task.FromResult<IReadOnlyList<RunEmbeddingMatch>>(matches);
    }

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
