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
    /// <summary>Returns the member (with role) when email+password match within the current tenant; otherwise null.</summary>
    Task<AuthUser?> ValidateAsync(string email, string password, CancellationToken ct = default);
}
