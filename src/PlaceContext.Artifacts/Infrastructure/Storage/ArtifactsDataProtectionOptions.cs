namespace PlaceContext.Artifacts.Infrastructure.Storage;

public sealed class ArtifactsDataProtectionOptions
{
    public const string SectionName = "PlaceContext:Artifacts:DataProtection";

    public string? KeyDirectory { get; set; }
}
