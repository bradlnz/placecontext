namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one node in the deep-dive dependency graph (pre-layout).</summary>
public sealed record GraphNodeView(string Id, string Label, int Degree, bool IsGod, string? Content = null);
