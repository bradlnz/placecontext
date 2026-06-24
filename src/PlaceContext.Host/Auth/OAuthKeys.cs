using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace PlaceContext.Host.Auth;

/// <summary>
/// RSA signing key for OAuth access tokens (RS256). Generated once per process and exposed as a JWKS so
/// the bearer middleware (and any client) can verify signatures. In-memory: tokens are invalidated on
/// restart — fine for a single-instance dev/host; a shared persisted key is needed for multi-instance.
/// </summary>
public static class OAuthKeys
{
    public static RsaSecurityKey SigningKey { get; }
    public static SigningCredentials SigningCredentials { get; }
    public static string KeyId { get; }

    static OAuthKeys()
    {
        var rsa = RSA.Create(2048);
        KeyId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        SigningKey = new RsaSecurityKey(rsa) { KeyId = KeyId };
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256);
    }

    /// <summary>The public key as a JWKS document.</summary>
    public static object Jwks()
    {
        var p = SigningKey.Rsa!.ExportParameters(false);
        return new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    kid = KeyId,
                    n = Base64UrlEncoder.Encode(p.Modulus!),
                    e = Base64UrlEncoder.Encode(p.Exponent!),
                }
            }
        };
    }
}
