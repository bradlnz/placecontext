namespace PlaceContext.AgentChat.Infrastructure.Persistence;

public sealed class ChatCommandRow : IAgentChatTenantOwned
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string? Args { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
