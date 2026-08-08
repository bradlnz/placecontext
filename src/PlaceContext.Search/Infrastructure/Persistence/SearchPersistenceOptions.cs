namespace PlaceContext.Search.Infrastructure.Persistence;

public sealed class SearchPersistenceOptions
{
    public const string SectionName = "PlaceContext:Search:Persistence";
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=placecontext_search;Username=postgres;Password=postgres";

    public string ConnectionString { get; set; } = DefaultConnectionString;
}
