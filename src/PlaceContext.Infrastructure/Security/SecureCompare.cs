using System.Security.Cryptography;
using System.Text;

namespace PlaceContext.Infrastructure.Security;

/// <summary>
/// Length-safe constant-time comparison. <see cref="CryptographicOperations.FixedTimeEquals"/> throws
/// when the two buffers differ in length, which turns a wrong key into a 500 and can act as an oracle.
/// Hashing both sides first yields fixed-length digests so the compare never throws.
/// </summary>
public static class SecureCompare
{
    public static bool Equals(string? presented, string? expected)
    {
        if (expected is null) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(presented ?? ""));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
