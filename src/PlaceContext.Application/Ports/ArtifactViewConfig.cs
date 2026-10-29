namespace PlaceContext.Application.Ports;

/// <summary>Workspace-specific presentation rules for the Artifacts page.</summary>
public sealed record ArtifactViewConfig(IReadOnlyList<ArtifactCategoryRule> Categories)
{
    public string? CategoryFor(string title) =>
        Categories.FirstOrDefault(category => category.Matches(title))?.Id;
}
