namespace PlaceContext.Projects.Infrastructure.Persistence;

public sealed class ProjectsPersistenceOptions
{
    public const string SectionName = "PlaceContext:Projects:Persistence";
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=placecontext;Username=placecontext;Password=placecontext";

    public string ConnectionString { get; set; } = DefaultConnectionString;
}
