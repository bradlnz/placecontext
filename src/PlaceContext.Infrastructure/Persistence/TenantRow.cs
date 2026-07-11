using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Tenant registry row. Not tenant-owned (it defines tenants).</summary>
public sealed class TenantRow
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string TimeZoneId { get; set; } = "UTC";
    public string? GitHubLogin { get; set; }
    public string? GitHubToken { get; set; }
    /// <summary>Whitelabel branding JSON ({productName, logoDataUri, colors…}); null = default look.</summary>
    public string? BrandingJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
