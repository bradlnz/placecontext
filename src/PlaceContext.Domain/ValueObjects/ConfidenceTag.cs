namespace PlaceContext.Domain.ValueObjects;

/// <summary>Confidence with which graphify asserted a relationship. Mirrors graphify's tagging.</summary>
public enum ConfidenceTag
{
    Extracted,
    Inferred,
    Ambiguous
}
