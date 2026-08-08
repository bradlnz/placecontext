namespace PlaceContext.AgentChat.Infrastructure.Persistence;

public sealed class AgentChatPersistenceOptions
{
    public const string SectionName = "PlaceContext:AgentChat:Persistence";
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=placecontext_agent_chat;Username=postgres;Password=postgres";

    public string ConnectionString { get; set; } = DefaultConnectionString;
}
