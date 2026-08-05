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
    /// <summary>Optional fully-qualified host serving this tenant's customer portal.</summary>
    public string? CustomerPortalDomain { get; set; }
    /// <summary>Whether external customer-portal accounts may be provisioned for this tenant.</summary>
    public bool CustomerPortalEnabled { get; set; }
    public string? GitHubLogin { get; set; }
    public string? GitHubToken { get; set; }
    /// <summary>Whitelabel branding JSON ({productName, logoDataUri, colors…}); null = default look.</summary>
    public string? BrandingJson { get; set; }

    /// <summary>Portal menu layout JSON (order, labels, visibility); null = built-in defaults.</summary>
    public string? MenuJson { get; set; }

    /// <summary>Artifacts page category/prefix rules JSON; null = built-in defaults.</summary>
    public string? ArtifactViewJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
