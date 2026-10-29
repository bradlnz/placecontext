namespace PlaceContext.Application.Ports;

/// <summary>
/// A named artifact filter. Prefixes are matched against artifact titles in their configured order;
/// the first matching category wins.
/// </summary>
public sealed record ArtifactCategoryRule(
    string Id,
    string Label,
    IReadOnlyList<string> Prefixes)
{
    public bool Matches(string title) =>
        Prefixes.Any(prefix =>
            !string.IsNullOrWhiteSpace(prefix) &&
            title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
