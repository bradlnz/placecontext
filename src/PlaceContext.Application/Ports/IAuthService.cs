using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Registration + credential validation for portal users. Operates within the current tenant.</summary>
public interface IAuthService
{
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    /// <summary>True once the organisation has at least one member — used to close self-registration after the owner.</summary>
    Task<bool> HasAnyMembersAsync(CancellationToken ct = default);
    /// <summary>Creates a member with the given role in the current tenant; returns null if the email is already taken.</summary>
    Task<AuthUser?> RegisterAsync(string email, string displayName, string password, UserRole role, CancellationToken ct = default);
    /// <summary>
    /// Returns the sole operator (Owner) for the current tenant, creating one on first use. Backs the
    /// token sign-in path: a self-hosted deployment has no registration, so the cluster operator who
    /// presents a valid portal token is signed in as this user.
    /// </summary>
    Task<AuthUser> GetOrCreateOperatorAsync(CancellationToken ct = default);
}
