namespace PlaceContext.Artifacts.Infrastructure.Persistence;

public sealed class ArtifactsPersistenceOptions
{
    public const string SectionName = "PlaceContext:Artifacts:Persistence";
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=placecontext_artifacts;Username=postgres;Password=postgres";

    public string ConnectionString { get; set; } = DefaultConnectionString;
}
