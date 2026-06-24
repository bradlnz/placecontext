using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Registration + credential validation for portal users. Operates within the current tenant.</summary>
public interface IAuthService
{
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    /// <summary>Creates a user in the current tenant; returns null if the email is already taken.</summary>
    Task<AuthUser?> RegisterAsync(string email, string displayName, string password, CancellationToken ct = default);
    /// <summary>Returns the user when the email+password match within the current tenant; otherwise null.</summary>
    Task<AuthUser?> ValidateAsync(string email, string password, CancellationToken ct = default);
}
