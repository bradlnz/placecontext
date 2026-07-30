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

/// <summary>Workspace-specific presentation rules for the Artifacts page.</summary>
public sealed record ArtifactViewConfig(IReadOnlyList<ArtifactCategoryRule> Categories)
{
    public string? CategoryFor(string title) =>
        Categories.FirstOrDefault(category => category.Matches(title))?.Id;
}

public interface IArtifactViewConfigService
{
    ArtifactViewConfig DefaultConfig();
    Task<ArtifactViewConfig> GetAsync(CancellationToken ct = default);
    Task SaveAsync(ArtifactViewConfig config, CancellationToken ct = default);
}
