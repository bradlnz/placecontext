using PlaceContext.Application.Cqrs;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListPendingOcrHandler
    : IQueryHandler<ListPendingOcrQuery, IReadOnlyList<PendingOcrArtifactView>>
{
    private readonly IRunArtifactLinkRepository _links;
    public ListPendingOcrHandler(IRunArtifactLinkRepository links) => _links = links;

    public async Task<IReadOnlyList<PendingOcrArtifactView>> HandleAsync(ListPendingOcrQuery q, CancellationToken ct = default)
    {
        var rows = await _links.ListPendingOcrAsync(q.Take, ct);
        return rows.Select(r => new PendingOcrArtifactView(
            r.Id, r.RunId, r.JobId, r.Title, r.ContentType, r.SizeBytes, r.CreatedAt,
            $"/runs/{r.RunId}/artifacts/{r.Id}")).ToList();
    }
}
