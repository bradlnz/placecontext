using PlaceContext.Application.Cqrs;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

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
