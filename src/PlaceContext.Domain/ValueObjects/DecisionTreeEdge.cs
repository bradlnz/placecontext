namespace PlaceContext.Domain.ValueObjects;

/// <summary>A parent → child edge in the knowledge graph. Confidence encodes how firmly it was derived.</summary>
public sealed record DecisionTreeEdge(string ParentId, string ChildId, ConfidenceTag Confidence);
