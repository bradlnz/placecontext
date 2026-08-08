namespace PlaceContext.Jobs.Infrastructure.Persistence;

public sealed class JobsPersistenceOptions
{
    public const string SectionName = "PlaceContext:Jobs:Persistence";
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=placecontext;Username=placecontext;Password=placecontext";

    public string ConnectionString { get; set; } = DefaultConnectionString;
}
