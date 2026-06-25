namespace PlaceContext.Application.Dtos;

/// <summary>Read model: the whole dependency graph for the deep-dive SVG.</summary>
public sealed record GraphVizView(
    Guid ProjectId,
    int NodeCount,
    int LinkCount,
    IReadOnlyList<GraphNodeView> Nodes,
    IReadOnlyList<GraphLinkView> Links);
