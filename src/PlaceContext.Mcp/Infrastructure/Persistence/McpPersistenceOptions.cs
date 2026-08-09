namespace PlaceContext.Mcp.Infrastructure.Persistence;

public sealed class McpPersistenceOptions
{
    public const string SectionName = "PlaceContext:Mcp:Persistence";
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=placecontext_mcp;Username=postgres;Password=postgres";

    public string ConnectionString { get; set; } = DefaultConnectionString;
}
