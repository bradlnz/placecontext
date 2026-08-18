using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetGraphVizHandler : IQueryHandler<GetGraphVizQuery, GraphVizView>
{
    private readonly IDecisionTreeProvider _tree;
    private readonly IRunArtifactLinkRepository _artifacts;

    public GetGraphVizHandler(IDecisionTreeProvider tree, IRunArtifactLinkRepository artifacts) =>
        (_tree, _artifacts) = (tree, artifacts);

    public async Task<GraphVizView> HandleAsync(GetGraphVizQuery query, CancellationToken ct = default)
    {
        var tree = await _tree.BuildAsync(ProjectId.From(query.ProjectId), ct);
        var g = tree.ToGraphView();
        IReadOnlyDictionary<Guid, RunArtifactLink> artifacts;
        try
        {
            artifacts = (await _artifacts.ListForProjectAsync(query.ProjectId, take: 200, ct: ct))
                .ToDictionary(artifact => artifact.Id);
        }
        catch
        {
            // The structural graph remains usable if the artifact store is temporarily unavailable.
            artifacts = new Dictionary<Guid, RunArtifactLink>();
        }

        var nodes = g.Nodes.Select(n => new GraphNodeView(
            n.Id,
            n.Label,
            n.Degree,
            n.IsGod,
            n.Content,
            n.Kind?.ToString(),
            Artifact: ArtifactReference(n.Id, artifacts)
        )).ToList();
        var links = g.Links.Select(l => new GraphLinkView(l.Source, l.Target, l.Confidence.ToString())).ToList();
        return new GraphVizView(query.ProjectId, nodes.Count, links.Count, nodes, links);
    }

    private static GraphNodeArtifactRef? ArtifactReference(
        string nodeId,
        IReadOnlyDictionary<Guid, RunArtifactLink> artifacts
    )
    {
        const string prefix = "artifact:";
        if (!nodeId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(nodeId[prefix.Length..], out var artifactId)
            || !artifacts.TryGetValue(artifactId, out var artifact))
            return null;

        return new GraphNodeArtifactRef(
            artifact.Id,
            artifact.RunId,
            artifact.Kind.ToString(),
            artifact.Title,
            artifact.ContentType,
            artifact.CreatedAt
        );
    }
}
