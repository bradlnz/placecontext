namespace PlaceContext.Agents.Infrastructure.Persistence;

public sealed class AgentsPersistenceOptions
{
    public const string SectionName = "PlaceContext:Agents:Persistence";
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=placecontext_agents;Username=postgres;Password=postgres";

    public string ConnectionString { get; set; } = DefaultConnectionString;
}
