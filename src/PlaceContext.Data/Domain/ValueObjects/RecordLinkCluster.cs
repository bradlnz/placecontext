namespace PlaceContext.Domain.ValueObjects;

/// <summary>One shared identity value (address, email, phone, etc.) and every project row that holds it.
/// Used by the data graph to weave entity-aligned link nodes between the business entities whose
/// records share the same value.</summary>
public sealed record RecordLinkCluster(
    string Kind,
    string NormalizedValue,
    string DisplayValue,
    IReadOnlyList<RecordLinkClusterOccurrence> Occurrences);
