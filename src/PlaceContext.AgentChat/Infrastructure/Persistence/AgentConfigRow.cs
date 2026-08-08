namespace PlaceContext.AgentChat.Infrastructure.Persistence;

public sealed class AgentConfigRow : IAgentChatTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string BaseModel { get; set; } = "qwen3.5:0.8b";
    public string SystemPrompt { get; set; } = string.Empty;
    public string Preamble { get; set; } = string.Empty;
    public string ToolCatalog { get; set; } = string.Empty;
    public string LaunchpadToolCatalog { get; set; } = string.Empty;
    public int MaxContextChunks { get; set; } = 5;
    public float Temperature { get; set; } = 0.7f;
    public float TopP { get; set; } = 0.9f;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
