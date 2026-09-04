using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>A stored artifact file with enough context to browse and open it (the file viewer).</summary>
public sealed record ArtifactFileView(
    Guid Id,
    Guid RunId,
    Guid JobId,
    Guid ProjectId,
    string Kind,
    string Title,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt);

/// <summary>The newest stored artifacts across every project — the Artifacts file viewer's feed.</summary>
public sealed record ListRecentArtifactsQuery(int Take = 100) : IQuery<IReadOnlyList<ArtifactFileView>>;

/// <summary>Every stored artifact for one project — the project-scoped file viewer (no global cap hiding
/// older files). <paramref name="Search"/>, when given, keeps only Title/Kind matches server-side, so a
/// search widens coverage beyond whatever this project's load happened to cap at.</summary>
public sealed record ListProjectArtifactsQuery(Guid ProjectId, int Take = 2000, string? Search = null) : IQuery<IReadOnlyList<ArtifactFileView>>;
