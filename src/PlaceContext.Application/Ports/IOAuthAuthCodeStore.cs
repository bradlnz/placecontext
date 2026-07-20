namespace PlaceContext.Application.Ports;

/// <summary>A short-lived authorization code bound to a user, tenant, role, client, and PKCE challenge.</summary>
public sealed record AuthCode(
    string Code, string ClientId, string RedirectUri, string CodeChallenge,
    Guid UserId, Guid TenantId, string Role, string Scope, DateTimeOffset Expires);

/// <summary>
/// Persistence of OAuth authorization codes so they survive server restarts and are shared across
/// replicas. Codes are single-use: <c>TakeAsync</c> atomically consumes the code so a replay or
/// race gets nothing.
/// </summary>
public interface IOAuthAuthCodeStore
{
    /// <summary>Persists a new authorization code. Expired codes are opportunistically purged.</summary>
    Task SaveAsync(AuthCode code, CancellationToken ct = default);

    /// <summary>Consumes a code (single use); returns null if missing or expired.</summary>
    Task<AuthCode?> TakeAsync(string code, DateTimeOffset now, CancellationToken ct = default);
}
