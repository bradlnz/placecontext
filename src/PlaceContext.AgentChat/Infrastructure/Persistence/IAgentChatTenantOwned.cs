namespace PlaceContext.AgentChat.Infrastructure.Persistence;

internal interface IAgentChatTenantOwned
{
    Guid TenantId { get; set; }
}
