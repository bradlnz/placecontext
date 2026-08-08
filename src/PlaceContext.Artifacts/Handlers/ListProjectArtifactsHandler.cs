using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListProjectArtifactsHandler : IQueryHandler<ListProjectArtifactsQuery, IReadOnlyList<ArtifactFileView>>
{
    private readonly IRunArtifactLinkRepository _links;

    public ListProjectArtifactsHandler(IRunArtifactLinkRepository links) => _links = links;

    public async Task<IReadOnlyList<ArtifactFileView>> HandleAsync(ListProjectArtifactsQuery query, CancellationToken ct = default)
        => (await _links.ListForProjectAsync(query.ProjectId, query.Take, query.Search, ct))
            .Select(ListRecentArtifactsHandler.Map)
            .ToList();
}
