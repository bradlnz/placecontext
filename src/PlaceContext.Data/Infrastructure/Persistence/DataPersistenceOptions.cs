namespace PlaceContext.Data.Infrastructure.Persistence;

public sealed class DataPersistenceOptions
{
    public const string SectionName = "PlaceContext:Data:Persistence";
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=placecontext_data;Username=postgres;Password=postgres";

    public string ConnectionString { get; set; } = DefaultConnectionString;
}
