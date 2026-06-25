using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Flat persistence row for a tenant-defined report template; sections are stored as JSON.</summary>
public sealed class ReportTemplateRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Sections { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
