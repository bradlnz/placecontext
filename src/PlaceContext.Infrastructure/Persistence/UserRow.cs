using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class UserRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    /// <summary>
    /// True only when a human chose this password (first-run /setup, invite acceptance, self-registration).
    /// The machine-provisioned "operator" row (see <c>AuthService.GetOrCreateOperatorAsync</c>) stores an
    /// unusable random hash and leaves this false, so it never counts as a configured admin — see
    /// <c>AuthService.IsUnconfiguredAsync</c>.
    /// </summary>
    public bool PasswordSet { get; set; }
    public string Role { get; set; } = "Member";   // UserRole name
    public DateTimeOffset CreatedAt { get; set; }
}
