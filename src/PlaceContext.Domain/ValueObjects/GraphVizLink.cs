namespace PlaceContext.Domain.ValueObjects;

/// <summary>An edge in a project's graph, with the confidence graphify asserted it.</summary>
public readonly record struct GraphVizLink(string Source, string Target, ConfidenceTag Confidence);
