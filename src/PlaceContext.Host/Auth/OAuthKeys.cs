using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace PlaceContext.Host.Auth;

/// <summary>
/// RSA signing key for OAuth access tokens (RS256), exposed as a JWKS so the bearer middleware (and any
/// client) can verify signatures. When <c>PlaceContext:OAuth:SigningKeyPem</c> is configured (a shared
/// cluster secret), every replica loads the SAME key with the SAME deterministic key id — otherwise each
/// replica signs with its own random key and a token issued by one pod is rejected by another. With no
/// PEM (single-instance dev) it falls back to an ephemeral key, regenerated per process.
/// </summary>
public static class OAuthKeys
{
    public static RsaSecurityKey SigningKey { get; private set; } = null!;
    public static SigningCredentials SigningCredentials { get; private set; } = null!;
    public static string KeyId { get; private set; } = "";

    static OAuthKeys() => Init(null);

    /// <summary>Load the signing key from a PEM (shared across replicas) or generate an ephemeral one.</summary>
    public static void Init(string? pem)
    {
        var rsa = RSA.Create(2048);
        if (!string.IsNullOrWhiteSpace(pem))
            rsa.ImportFromPem(pem);
        // Deterministic key id from the public modulus, so replicas sharing the key agree on the kid
        // (clients cache JWKS by kid; a mismatched kid looks like an unknown key → rejected).
        var modulus = rsa.ExportParameters(false).Modulus!;
        KeyId = Convert.ToHexString(SHA256.HashData(modulus))[..16].ToLowerInvariant();
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
