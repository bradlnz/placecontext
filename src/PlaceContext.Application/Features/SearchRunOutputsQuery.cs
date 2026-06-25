using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Semantic search over a project's job-run outputs: embeds the query text and returns the nearest
/// run outputs by cosine similarity. Returns empty when embeddings are not configured.
/// </summary>
public sealed record SearchRunOutputsQuery(Guid ProjectId, string Query, int Take = 10)
    : IQuery<IReadOnlyList<RunOutputMatchView>>;
