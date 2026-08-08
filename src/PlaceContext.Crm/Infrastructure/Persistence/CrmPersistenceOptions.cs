namespace PlaceContext.Crm.Infrastructure.Persistence;

public sealed class CrmPersistenceOptions
{
    public const string SectionName = "PlaceContext:Crm:Persistence";
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=placecontext;Username=placecontext;Password=placecontext";

    public string ConnectionString { get; set; } = DefaultConnectionString;
}
