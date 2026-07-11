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
            .Select(l => new ArtifactFileView(l.Id, l.RunId, l.JobId, l.ProjectId, l.Kind.ToString(),
                l.Title, l.ContentType, l.SizeBytes, l.CreatedAt))
            .ToList();
}
