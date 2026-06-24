namespace PlaceContext.Domain.ValueObjects;

/// <summary>A parent → child edge in the decision tree. Confidence encodes how firmly it was derived.</summary>
public sealed record DecisionTreeEdge(string ParentId, string ChildId, ConfidenceTag Confidence);
