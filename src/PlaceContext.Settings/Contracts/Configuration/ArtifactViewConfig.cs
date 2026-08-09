namespace PlaceContext.Settings.Contracts.Configuration;

public sealed record ArtifactViewConfig(IReadOnlyList<ArtifactCategoryRule> Categories);
