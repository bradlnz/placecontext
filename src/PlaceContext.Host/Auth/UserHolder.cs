using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Auth;

/// <summary>Holds the request/circuit's resolved user identity in scoped DI — mirrors
/// <see cref="PlaceContext.Host.Tenancy.TenantHolder"/> — so a Blazor circuit can re-apply it to the
/// ambient <see cref="CurrentUser"/> on every inbound activity.</summary>
public sealed class UserHolder
{
    public UserIdentity? User { get; set; }
}
