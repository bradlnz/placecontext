namespace PlaceContext.Application.Ports;

/// <summary>Personal API tokens members mint from Settings for the entity data API.</summary>
public interface IUserApiTokenService
{
    /// <summary>Creates a token for the current user. Returns the raw token once (never stored).</summary>
    Task<CreatedUserApiToken> CreateAsync(string name, TimeSpan? lifetime, CancellationToken ct = default);

    Task<IReadOnlyList<UserApiTokenView>> ListMineAsync(CancellationToken ct = default);

    Task<bool> RevokeAsync(Guid tokenId, CancellationToken ct = default);

    /// <summary>
    /// Validates a presented raw token against the store for the current tenant. On match returns the
    /// owning user's identity (id + role); otherwise null. Updates LastUsedAt on success.
    /// </summary>
    Task<AuthUser?> ValidateAsync(string rawToken, CancellationToken ct = default);
}

/// <summary>Metadata for a stored token — never includes the raw secret.</summary>
public sealed record UserApiTokenView(
    Guid Id,
    string Name,
    string TokenPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of minting a token — <see cref="RawToken"/> is shown once then discarded.</summary>
public sealed record CreatedUserApiToken(
    Guid Id,
    string Name,
    string RawToken,
    string TokenPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);
