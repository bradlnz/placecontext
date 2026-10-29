using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>The newest stored artifacts across every project — the Artifacts file viewer's feed.</summary>
public sealed record ListRecentArtifactsQuery(int Take = 100, int Skip = 0, string? Search = null) : IQuery<IReadOnlyList<ArtifactFileView>>;
