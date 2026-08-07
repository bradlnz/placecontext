namespace PlaceContext.Infrastructure.Persistence;

public sealed class CrmClientJobChainAssignmentRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ClientId { get; set; }
    public Guid ChainId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
