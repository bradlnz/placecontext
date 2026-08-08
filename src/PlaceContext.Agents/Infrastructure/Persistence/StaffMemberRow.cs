namespace PlaceContext.Agents.Infrastructure.Persistence;

public sealed class StaffMemberRow : IAgentsTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProjectIdsJson { get; set; } = "[]";
    public string InstructionsOverride { get; set; } = string.Empty;
    public string? ModelOverride { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
