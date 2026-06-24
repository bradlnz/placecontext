namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// A whole-graph projection for the deep-dive visualization: the nodes and links of a project's
/// graphify graph. Layout (x/y) is a presentation concern computed by the view, not stored here.
/// </summary>
public sealed record GraphView
{
    public IReadOnlyList<GraphVizNode> Nodes { get; }
    public IReadOnlyList<GraphVizLink> Links { get; }

    private GraphView(IReadOnlyList<GraphVizNode> nodes, IReadOnlyList<GraphVizLink> links)
    {
        Nodes = nodes;
        Links = links;
    }

    public static readonly GraphView Empty = new(Array.Empty<GraphVizNode>(), Array.Empty<GraphVizLink>());

    public static GraphView Of(IEnumerable<GraphVizNode> nodes, IEnumerable<GraphVizLink> links)
        => new(nodes.ToList(), links.ToList());
}
