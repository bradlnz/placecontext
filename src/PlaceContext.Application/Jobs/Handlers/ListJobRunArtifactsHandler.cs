using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed class ListJobRunArtifactsHandler
    : IQueryHandler<ListJobRunArtifactsQuery, IReadOnlyList<RunArtifactLinkView>>
{
    private readonly PlaceContext.Domain.Repositories.IRunArtifactLinkRepository _links;
    public ListJobRunArtifactsHandler(PlaceContext.Domain.Repositories.IRunArtifactLinkRepository links) => _links = links;

    public async Task<IReadOnlyList<RunArtifactLinkView>> HandleAsync(ListJobRunArtifactsQuery q, CancellationToken ct = default)
    {
        var rows = await _links.ListForJobAsync(q.JobId, ct);
        return rows.Select(r => new RunArtifactLinkView(
            r.Id, r.RunId, r.Kind, r.Title, r.ContentType, r.SizeBytes, r.CreatedAt)).ToList();
    }
}
