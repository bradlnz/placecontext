using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>A post-job output stored for a run, surfaced as an openable link in the portal/TUI.</summary>
public sealed record RunArtifactLinkView(
    Guid Id,
    Guid RunId,
    PlaceContext.Domain.ValueObjects.PostJobActionKind Kind,
    string Title,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt);

/// <summary>Lists the post-job artifacts produced for a run (newest action order).</summary>
public sealed record ListRunArtifactsQuery(Guid RunId) : IQuery<IReadOnlyList<RunArtifactLinkView>>;

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
