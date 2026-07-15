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

public sealed class ListRecentArtifactsHandler : IQueryHandler<ListRecentArtifactsQuery, IReadOnlyList<ArtifactFileView>>
{
    private readonly IRunArtifactLinkRepository _links;

    public ListRecentArtifactsHandler(IRunArtifactLinkRepository links) => _links = links;

    public async Task<IReadOnlyList<ArtifactFileView>> HandleAsync(ListRecentArtifactsQuery query, CancellationToken ct = default)
        => (await _links.ListRecentAsync(query.Take, ct))
            .Select(Map)
            .ToList();

    internal static ArtifactFileView Map(Domain.Entities.RunArtifactLink l) => new(
        l.Id, l.RunId, l.JobId, l.ProjectId, l.Kind.ToString(),
        l.Title, l.ContentType, l.SizeBytes, l.CreatedAt);
}

/// <summary>Every stored artifact for one project — the project-scoped file viewer (no global cap hiding older files).</summary>
public sealed record ListProjectArtifactsQuery(Guid ProjectId, int Take = 2000) : IQuery<IReadOnlyList<ArtifactFileView>>;

public sealed class ListProjectArtifactsHandler : IQueryHandler<ListProjectArtifactsQuery, IReadOnlyList<ArtifactFileView>>
{
    private readonly IRunArtifactLinkRepository _links;

    public ListProjectArtifactsHandler(IRunArtifactLinkRepository links) => _links = links;

    public async Task<IReadOnlyList<ArtifactFileView>> HandleAsync(ListProjectArtifactsQuery query, CancellationToken ct = default)
        => (await _links.ListForProjectAsync(query.ProjectId, query.Take, ct))
            .Select(ListRecentArtifactsHandler.Map)
            .ToList();
}
