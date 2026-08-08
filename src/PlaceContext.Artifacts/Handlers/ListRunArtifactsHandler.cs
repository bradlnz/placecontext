using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed class ListRunArtifactsHandler
    : IQueryHandler<ListRunArtifactsQuery, IReadOnlyList<RunArtifactLinkView>>
{
    private readonly PlaceContext.Domain.Repositories.IRunArtifactLinkRepository _links;
    public ListRunArtifactsHandler(PlaceContext.Domain.Repositories.IRunArtifactLinkRepository links) => _links = links;

    public async Task<IReadOnlyList<RunArtifactLinkView>> HandleAsync(ListRunArtifactsQuery q, CancellationToken ct = default)
    {
        var rows = await _links.ListForRunAsync(q.RunId, ct);
        return rows.Select(r => new RunArtifactLinkView(
            r.Id, r.RunId, r.Kind, r.Title, r.ContentType, r.SizeBytes, r.CreatedAt)).ToList();
    }
}
