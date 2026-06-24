using System.Collections.Concurrent;

namespace PlaceContext.Host.Auth;

/// <summary>
/// In-memory registry of pending authorization codes. Codes are single-use and expire within minutes,
/// so losing them on restart is harmless (the client simply re-runs the authorize step). Client
/// registrations, which must survive restarts, are persisted via <c>IOAuthClientStore</c> instead.
/// </summary>
public sealed class OAuthStore
{
    private readonly ConcurrentDictionary<string, AuthCode> _codes = new();

    public void SaveCode(AuthCode code) => _codes[code.Code] = code;

    /// <summary>Consumes a code (single use); returns null if missing or expired.</summary>
    public AuthCode? TakeCode(string code, DateTimeOffset now)
        => _codes.TryRemove(code, out var c) && c.Expires > now ? c : null;
}
