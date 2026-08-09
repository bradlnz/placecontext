using System.Collections.Concurrent;

namespace PlaceContext.Identity.Auth;

/// <summary>
/// Minimal brute-force friction for the password-login endpoint: a fixed delay on every failed attempt
/// that grows with repeated failures against the same email. Deliberately simple — an in-memory,
/// per-process counter (reset on restart, not shared across replicas) rather than a full distributed
/// rate limiter. It costs a naive credential-stuffing loop real wall-clock time with zero extra
/// infrastructure; it is not a substitute for a WAF/rate-limiter in front of a public deployment.
/// </summary>
public static class LoginThrottle
{
    private static readonly ConcurrentDictionary<string, int> Failures = new();

    /// <summary>Call after a failed login attempt; awaits a delay before the caller responds.</summary>
    public static Task PenalizeAsync(string email)
    {
        var key = Normalize(email);
        var attempts = Failures.AddOrUpdate(key, 1, static (_, n) => n + 1);
        // 300ms base, +150ms per prior failure this process has seen, capped at 3s so a genuine typo
        // doesn't turn into an unreasonable wait.
        var delayMs = Math.Min(300 + 150 * (attempts - 1), 3000);
        return Task.Delay(delayMs);
    }

    /// <summary>Call after a successful login to stop counting a legitimate user's earlier typos.</summary>
    public static void Clear(string email) => Failures.TryRemove(Normalize(email), out _);

    private static string Normalize(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();
}
