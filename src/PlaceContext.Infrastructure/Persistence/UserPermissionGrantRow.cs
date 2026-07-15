using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>A single explicit permission override for one user: allow (true) or revoke (false) on top
/// of their role's defaults. One row per (tenant, user, permission).</summary>
public sealed class UserPermissionGrantRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Permission { get; set; } = "";
    public bool Allowed { get; set; }
}
