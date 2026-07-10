namespace PlaceContext.Domain.ValueObjects;

/// <summary>A node in a project's knowledge graph, projected for analysis and visualization.</summary>
public sealed record DecisionTreeNode(string Id, string Label, TreeNodeKind Kind, int Degree, bool IsHotspot,
    string? Content = null);
