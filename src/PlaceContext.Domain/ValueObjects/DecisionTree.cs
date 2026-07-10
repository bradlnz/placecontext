namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// A project's knowledge graph, assembled from everything PlaceContext has logged: decisions branch off
/// the root, the changes they led to hang beneath them, the files/nodes those changes touched hang
/// beneath the changes, and a tool-call activity branch summarizes MCP traffic. This replaces the
/// external graphify knowledge graph — it is derived purely from recorded activity, not from parsing
/// code. The headline projections (<see cref="ToGraphView"/>, <see cref="ToMetrics"/>,
/// <see cref="Hotspots"/>) keep the existing graph-shaped consumers working.
/// </summary>
public sealed record DecisionTree
{
    public IReadOnlyList<DecisionTreeNode> Nodes { get; }
    public IReadOnlyList<DecisionTreeEdge> Edges { get; }

    private DecisionTree(IReadOnlyList<DecisionTreeNode> nodes, IReadOnlyList<DecisionTreeEdge> edges)
    {
        Nodes = nodes;
        Edges = edges;
    }

    public static readonly DecisionTree Empty =
        new(Array.Empty<DecisionTreeNode>(), Array.Empty<DecisionTreeEdge>());

    public static DecisionTree Of(IEnumerable<DecisionTreeNode> nodes, IEnumerable<DecisionTreeEdge> edges)
        => new(nodes.ToList(), edges.ToList());

    /// <summary>Projects the tree onto the node/link view the deep-dive SVG renders.</summary>
    public GraphView ToGraphView()
        => GraphView.Of(
            Nodes.Select(n => new GraphVizNode(n.Id, n.Label, n.Degree, n.IsHotspot, n.Content)),
            Edges.Select(e => new GraphVizLink(e.ParentId, e.ChildId, e.Confidence)));

    /// <summary>The churn hotspots — files/nodes touched by many changes — surfaced as god nodes.</summary>
    public IReadOnlyList<GodNode> Hotspots()
        => Nodes.Where(n => n.IsHotspot)
            .OrderByDescending(n => n.Degree)
            .Select(n => GodNode.Of(GraphNodeId.From(n.Id), NormLabel.From(n.Label), n.Degree))
            .ToList();

    /// <summary>Structural metrics the technical-risk scorer reads, distilled from the tree shape.</summary>
    public GraphMetrics ToMetrics()
    {
        var nodeCount = Nodes.Count;
        var linkCount = Edges.Count;
        var hotspots = Nodes.Count(n => n.IsHotspot);
        var avgDegree = nodeCount == 0 ? 0.0 : (double)linkCount * 2 / nodeCount;

        // The low-confidence ratio is a *coupling-provenance* signal (orphan changes, failed tool calls),
        // so it ignores the run-output "brain" subgraph: its semantic similarity links are legitimately
        // Inferred but say nothing about code coupling, and must not inflate technical risk.
        var brain = Nodes.Where(n => n.Kind == TreeNodeKind.JobRunOutput).Select(n => n.Id).ToHashSet();
        var coupling = Edges.Where(e => !brain.Contains(e.ParentId) && !brain.Contains(e.ChildId)).ToList();
        var lowConf = coupling.Count == 0 ? 0.0
            : (double)coupling.Count(e => e.Confidence != ConfidenceTag.Extracted) / coupling.Count;
        return GraphMetrics.From(nodeCount, linkCount, hotspots, avgDegree, lowConf);
    }

    /// <summary>
    /// Answers a structured question about the tree in-process — the replacement for graphify's NL
    /// query. Recognizes a small vocabulary (hotspots, decisions, changes, activity, unverified);
    /// anything else gets a one-line summary.
    /// </summary>
    public string Answer(string question)
    {
        var q = (question ?? string.Empty).ToLowerInvariant();

        if (Has(q, "hotspot", "churn", "god"))
        {
            var hot = Nodes.Where(n => n.IsHotspot).OrderByDescending(n => n.Degree).ToList();
            return hot.Count == 0
                ? "No churn hotspots: no file has been touched by enough changes yet."
                : "Churn hotspots:\n" + string.Join("\n", hot.Select(n => $"  • {Strip(n.Label)} — {n.Degree} changes"));
        }

        if (Has(q, "decision"))
        {
            var decisions = Nodes.Where(n => n.Kind == TreeNodeKind.Decision).ToList();
            return decisions.Count == 0
                ? "No decisions have been recorded for this project."
                : "Decisions:\n" + string.Join("\n", decisions.Select(n => $"  • {n.Label}"));
        }

        if (Has(q, "unverified", "orphan", "confidence", "risk"))
        {
            var low = Edges.Count(e => e.Confidence != ConfidenceTag.Extracted);
            return $"{low} of {Edges.Count} links are low-confidence (orphan changes or failed tool calls).";
        }

        if (Has(q, "brain", "memory", "run output", "output", "semantic"))
        {
            var outs = Nodes.Where(n => n.Kind == TreeNodeKind.JobRunOutput).OrderByDescending(n => n.Degree).ToList();
            return outs.Count == 0
                ? "No job-run outputs have been woven into the graph yet."
                : $"Run-output brain nodes ({outs.Count}):\n"
                    + string.Join("\n", outs.Take(10).Select(n => $"  • {n.Label} — linked to {n.Degree}"));
        }

        if (Has(q, "tool", "activity", "traffic"))
        {
            var tools = Nodes.Where(n => n.Kind == TreeNodeKind.Tool).OrderByDescending(n => n.Degree).ToList();
            return tools.Count == 0
                ? "No MCP tool activity has been logged for this project."
                : "Tool activity:\n" + string.Join("\n", tools.Select(n => $"  • {n.Label}"));
        }

        return $"{Nodes.Count} nodes, {Edges.Count} links: "
            + $"{Nodes.Count(n => n.Kind == TreeNodeKind.Decision)} decisions, "
            + $"{Nodes.Count(n => n.Kind == TreeNodeKind.Change)} changes, "
            + $"{Nodes.Count(n => n.IsHotspot)} hotspots.";
    }

    private static bool Has(string q, params string[] terms) => terms.Any(q.Contains);

    private static string Strip(string label)
    {
        var i = label.IndexOf(':');
        return i >= 0 && i < label.Length - 1 ? label[(i + 1)..] : label;
    }
}
