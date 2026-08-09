using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace PlaceContext.Identity.Auth;

/// <summary>
/// Validates the short-lived, HMAC-signed sign-in token the pctl TUI hands to the portal. The TUI mints
/// the token from the shared <c>PlaceContext:Portal:SigningKey</c> (a cluster secret it reads via
/// kubectl) and opens <c>/auth/portal?token=…</c>; the host re-derives the HMAC and, on a match within
/// the expiry window, signs the operator into the cookie. Symmetric (HMAC-SHA256) on purpose: the OAuth
/// RSA key is regenerated per process and never leaves the host, so a Go client can't sign with it.
///
/// Token format: <c>{expUnixSeconds}.{nonceHex}.{base64url(HMACSHA256(key, "{exp}.{nonce}"))}</c>.
/// </summary>
public sealed class PortalToken
{
    // 60s replay window is plenty for "mint → open browser → land", and bounds how long a leaked token
    // is useful. Within that window each nonce is accepted once (below).
    private static readonly TimeSpan MaxClockGrace = TimeSpan.FromSeconds(5);

    // Nonces already redeemed, with the moment they expire — pruned lazily so the set can't grow forever.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usedNonces = new();

    /// <summary>
    /// True when <paramref name="token"/> carries a valid signature for <paramref name="signingKey"/>,
    /// has not expired, and its nonce has not been redeemed before (single-use). Constant-time on the
    /// signature comparison. Returns false (never throws) for any malformed input.
    /// </summary>
    public bool TryValidate(string? token, string? signingKey, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(signingKey))
            return false;

        var parts = token.Split('.');
        if (parts.Length != 3)
            return false;

        var (expPart, noncePart, sigPart) = (parts[0], parts[1], parts[2]);
        if (!long.TryParse(expPart, out var expUnix))
            return false;

        var expires = DateTimeOffset.FromUnixTimeSeconds(expUnix);
        if (expires < now || expires > now.Add(MaxClockGrace).AddMinutes(5))
            // Expired, or an implausibly far-future expiry (a token minted with a much longer TTL than we
            // issue) — reject rather than honour an attacker-chosen lifetime.
            return false;

        var payload = $"{expPart}.{noncePart}";
        var expected = ComputeSignature(signingKey, payload);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(sigPart), Encoding.ASCII.GetBytes(expected)))
            return false;

        // Single-use: redeem the nonce, refusing a replay within the validity window.
        PruneExpired(now);
        return _usedNonces.TryAdd(noncePart, expires);
    }

    private static string ComputeSignature(string signingKey, string payload)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var (nonce, expires) in _usedNonces)
            if (expires < now)
                _usedNonces.TryRemove(nonce, out _);
    }
}
