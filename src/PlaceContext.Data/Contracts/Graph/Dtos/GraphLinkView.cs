namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one edge in the deep-dive dependency graph.</summary>
public sealed record GraphLinkView(string Source, string Target, string Confidence);
