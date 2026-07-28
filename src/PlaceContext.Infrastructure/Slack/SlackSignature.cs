using System.Security.Cryptography;
using System.Text;

namespace PlaceContext.Infrastructure.Slack;

/// <summary>Verifies Slack Events API request signatures (v0 HMAC-SHA256).</summary>
public static class SlackSignature
{
    private static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(5);

    public static bool IsValid(string signingSecret, string? timestampHeader, string? signatureHeader, ReadOnlySpan<byte> body, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(signingSecret)
            || string.IsNullOrWhiteSpace(timestampHeader)
            || string.IsNullOrWhiteSpace(signatureHeader)
            || !long.TryParse(timestampHeader, out var tsUnix))
            return false;

        var requestTime = DateTimeOffset.FromUnixTimeSeconds(tsUnix);
        if ((now - requestTime).Duration() > MaxSkew)
            return false;

        if (!signatureHeader.StartsWith("v0=", StringComparison.Ordinal))
            return false;

        var baseString = $"v0:{timestampHeader}:{Encoding.UTF8.GetString(body)}";
        var key = Encoding.UTF8.GetBytes(signingSecret);
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(baseString));
        var expected = "v0=" + Convert.ToHexString(hash).ToLowerInvariant();

        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(signatureHeader);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
