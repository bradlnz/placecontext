namespace PlaceContext.Application.Auth;

/// <summary>
/// Server-side password-strength policy for the operator/admin password chosen during first-run setup
/// (and re-checked defensively wherever an admin password is created — never trust the client alone).
/// Deliberately simple and dependency-free: no external NIST/HIBP breach-list lookup, just a minimum
/// length plus a small denylist and a low-entropy heuristic. That combination rejects the overwhelming
/// majority of "admin/admin"-style trivial choices without pulling in a large external wordlist or a
/// network call from a security-sensitive path.
/// </summary>
public static class PasswordPolicy
{
    /// <summary>Minimum admin password length. 12 chars is the current OWASP ASVS baseline for a
    /// single-factor password with no other compensating control.</summary>
    public const int MinLength = 12;

    // A short list of the passwords people actually type into "choose an admin password" prompts. Not
    // meant to be exhaustive — the length + low-variety checks below catch most of the rest.
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password12", "password123", "password1234",
        "administrator", "admin12345", "admin123456", "letmein12345",
        "qwertyuiop12", "qwertyuiop123", "123456789012", "1234567890123",
        "changeme1234", "changeme123", "welcome12345", "welcome123456",
        "iloveyou1234", "abcdefghijkl", "trustno1trustno1", "passw0rd1234",
        "correcthorsebattery", "letmeinletmein",
    };

    /// <summary>
    /// Validates a candidate admin password (and, when supplied, its confirmation). Returns null when
    /// the password is acceptable, else a short user-facing reason.
    /// </summary>
    public static string? Validate(string? password, string? confirm = null)
    {
        password ??= string.Empty;
        if (password.Length < MinLength)
            return $"Choose a password of at least {MinLength} characters.";
        if (Common.Contains(password.Trim()))
            return "That password is too common — choose something less guessable.";
        if (IsLowVariety(password))
            return "Choose a password that isn't just a repeated or sequential pattern.";
        if (confirm is not null && !string.Equals(password, confirm, StringComparison.Ordinal))
            return "Passwords do not match.";
        return null;
    }

    // Cheap heuristic (not a full entropy estimator) that catches strings which pass the length check but
    // are still trivial: "aaaaaaaaaaaa" / "abababababab" (too few distinct characters) and
    // "abcdefghijkl" / "123456789012" (every character one more than the last).
    private static bool IsLowVariety(string password)
    {
        if (password.Distinct().Count() <= 3)
            return true;

        var sequential = true;
        for (var i = 1; i < password.Length; i++)
        {
            if (password[i] - password[i - 1] != 1)
            {
                sequential = false;
                break;
            }
        }
        return sequential;
    }
}
