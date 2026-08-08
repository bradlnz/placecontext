using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>The newest stored artifacts across every project — the Artifacts file viewer's feed.</summary>
public sealed record ListRecentArtifactsQuery(int Take = 100) : IQuery<IReadOnlyList<ArtifactFileView>>;
