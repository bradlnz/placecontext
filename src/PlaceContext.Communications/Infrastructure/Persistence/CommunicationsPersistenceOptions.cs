namespace PlaceContext.Communications.Infrastructure.Persistence;

public sealed class CommunicationsPersistenceOptions
{
    public const string SectionName = "PlaceContext:Communications:Persistence";
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=placecontext;Username=placecontext;Password=placecontext";
    public string ConnectionString { get; set; } = DefaultConnectionString;
}
