using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>One public lead-ingestion credential and browser origin per CRM project.</summary>
public sealed class CrmIngestionSettingsRow : ITenantOwned
{
    public Guid ProjectId { get; set; }
    public Guid TenantId { get; set; }
    public string AllowedOrigin { get; set; } = "";
    public string? TokenHash { get; set; }
    public string? TokenPrefix { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
