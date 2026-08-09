namespace PlaceContext.Application.Features;

/// <summary>All occurrences of one value: the group a normalized value forms across a project's tables.</summary>
public sealed record RecordLinkGroup(string Kind, string NormalizedValue, string DisplayValue,
    IReadOnlyList<RecordLink> Occurrences);
