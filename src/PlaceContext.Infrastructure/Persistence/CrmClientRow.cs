namespace PlaceContext.Infrastructure.Persistence;

public sealed class CrmClientRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string? Company { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string LifecycleStage { get; set; } = "Lead";
    public string? Notes { get; set; }
    public bool CustomerPortalEnabled { get; set; }
    public string? CustomerPortalSlug { get; set; }
    public string? CustomerPortalDomain { get; set; }
    public string? CustomerPortalBrandName { get; set; }
    public string? CustomerPortalLogoUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
