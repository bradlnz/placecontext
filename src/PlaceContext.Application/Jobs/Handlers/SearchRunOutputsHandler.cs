using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class SearchRunOutputsHandler
    : IQueryHandler<SearchRunOutputsQuery, IReadOnlyList<RunOutputMatchView>>
{
    private readonly IEmbeddingGateway _embeddings;
    private readonly IRunEmbeddingRepository _store;

    public SearchRunOutputsHandler(IEmbeddingGateway embeddings, IRunEmbeddingRepository store)
    {
        _embeddings = embeddings;
        _store = store;
    }

    public async Task<IReadOnlyList<RunOutputMatchView>> HandleAsync(
        SearchRunOutputsQuery query, CancellationToken ct = default)
    {
        if (!_embeddings.IsEnabled || string.IsNullOrWhiteSpace(query.Query))
            return Array.Empty<RunOutputMatchView>();

        var vectors = await _embeddings.EmbedAsync(new[] { query.Query }, ct);
        if (vectors.Count == 0) return Array.Empty<RunOutputMatchView>();

        var matches = await _store.SearchAsync(query.ProjectId, vectors[0], query.Take, ct);
        return matches
            .Select(m => new RunOutputMatchView(
                m.Embedding.JobRunId, m.Embedding.JobId, m.Embedding.Text, m.Score, m.Embedding.CreatedAt))
            .ToList();
    }
}
