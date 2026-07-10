using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>Lists the post-job artifacts produced for a run (newest action order).</summary>
public sealed record ListRunArtifactsQuery(Guid RunId) : IQuery<IReadOnlyList<RunArtifactLinkView>>;
