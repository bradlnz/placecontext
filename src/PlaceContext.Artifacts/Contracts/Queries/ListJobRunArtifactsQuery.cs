using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>Lists every run's post-job artifacts for one job (newest first) — the run-history panel's inline charts.</summary>
public sealed record ListJobRunArtifactsQuery(Guid JobId) : IQuery<IReadOnlyList<RunArtifactLinkView>>;
