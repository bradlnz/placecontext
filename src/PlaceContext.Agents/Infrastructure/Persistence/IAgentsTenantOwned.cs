namespace PlaceContext.Agents.Infrastructure.Persistence;

internal interface IAgentsTenantOwned
{
    Guid TenantId { get; set; }
}
