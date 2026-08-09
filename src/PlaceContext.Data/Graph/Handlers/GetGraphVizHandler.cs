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

    public GetGraphVizHandler(IDecisionTreeProvider tree) => _tree = tree;

    public async Task<GraphVizView> HandleAsync(GetGraphVizQuery query, CancellationToken ct = default)
    {
        var tree = await _tree.BuildAsync(ProjectId.From(query.ProjectId), ct);
        var g = tree.ToGraphView();
        var nodes = g.Nodes.Select(n => new GraphNodeView(n.Id, n.Label, n.Degree, n.IsGod, n.Content, n.Kind?.ToString())).ToList();
        var links = g.Links.Select(l => new GraphLinkView(l.Source, l.Target, l.Confidence.ToString())).ToList();
        return new GraphVizView(query.ProjectId, nodes.Count, links.Count, nodes, links);
    }
}
