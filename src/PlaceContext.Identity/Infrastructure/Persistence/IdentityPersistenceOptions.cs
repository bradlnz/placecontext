namespace PlaceContext.Identity.Infrastructure.Persistence;

public sealed class IdentityPersistenceOptions
{
    public const string SectionName = "PlaceContext:Identity:Persistence";
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=placecontext;Username=placecontext;Password=placecontext";

    public string ConnectionString { get; set; } = DefaultConnectionString;
}
