using System.Security.Cryptography;
using System.Text;

namespace PlaceContext.Infrastructure.Connections;

/// <summary>Pure OAuth2 PKCE S256/state primitives, separate from provider HTTP and storage.</summary>
public static class ManagedOAuthProtocol
{
    public static string RandomVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static string Challenge(string verifier)
    {
        if (string.IsNullOrWhiteSpace(verifier) || verifier.Length is < 43 or > 128)
            throw new ArgumentException("PKCE verifier must contain 43-128 characters.", nameof(verifier));
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }
    public static string RandomState() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static string HashState(string state) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(state)));
    private static string Base64Url(byte[] data) => Convert.ToBase64String(data)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
