using System.ComponentModel.DataAnnotations;

namespace PlaceContext.CustomerPortal;

public sealed class CustomerPortalOptions
{
    /// <summary>Tenant UUID assigned to this portal deployment. A portal never provisions by host.</summary>
    [Required]
    public Guid TenantId { get; set; }

    /// <summary>Exact public host routed to this deployment.</summary>
    [Required, MinLength(3)]
    public string Domain { get; set; } = "";
}
