namespace PlaceContext.Domain.ValueObjects;

/// <summary>A node in a project's graph, projected for visualization.</summary>
public readonly record struct GraphVizNode(string Id, string Label, int Degree, bool IsGod, string? Content = null,
    /// <summary>What the node represents (root, job, table, chain, decision, …) when the source graph knows.</summary>
    TreeNodeKind? Kind = null);
