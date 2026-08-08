namespace PlaceContext.AgentChat.Infrastructure.Persistence;

public sealed class AgentChatSessionRow : IAgentChatTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? UserId { get; set; }
    public string? Title { get; set; }
    public string MessagesJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
