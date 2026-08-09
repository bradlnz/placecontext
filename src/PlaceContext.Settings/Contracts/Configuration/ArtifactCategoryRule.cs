namespace PlaceContext.Settings.Contracts.Configuration;

public sealed record ArtifactCategoryRule(
    string Id,
    string Label,
    IReadOnlyList<string> Prefixes);
