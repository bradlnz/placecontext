namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one node in the deep-dive dependency graph (pre-layout).</summary>
public sealed record GraphNodeView(string Id, string Label, int Degree, bool IsGod, string? Content = null,
    /// <summary>Optional palette kind ("good" | "human") for typed nodes (artifacts, related records).</summary>
    string? Kind = null,
    /// <summary>Always draw this node's label (hubs and hovered nodes are labeled regardless).</summary>
    bool Labeled = false);
