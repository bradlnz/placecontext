using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>Every stored artifact for one project — the project-scoped file viewer (no global cap hiding
/// older files). <paramref name="Search"/>, when given, keeps only Title/Kind matches server-side, so a
/// search widens coverage beyond whatever this project's load happened to cap at.</summary>
public sealed record ListProjectArtifactsQuery(Guid ProjectId, int Take = 2000, string? Search = null) : IQuery<IReadOnlyList<ArtifactFileView>>;
