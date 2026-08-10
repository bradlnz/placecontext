namespace PlaceContext.Infrastructure.Persistence;

public sealed class AgentDefinitionRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Kind { get; set; } = "Worker";
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string TemplateKey { get; set; } = string.Empty;
    public string CapabilitiesJson { get; set; } = "[]";
    public string AllowedJobIdsJson { get; set; } = "[]";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
