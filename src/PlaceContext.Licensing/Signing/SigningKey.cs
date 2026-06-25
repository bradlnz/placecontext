using System.Security.Cryptography;

namespace PlaceContext.Licensing.Signing;

/// <summary>An ECDSA P-256 signing key with its public SubjectPublicKeyInfo and a derived key id (kid).
/// The kid is a short, stable fingerprint of the public key so rotation entries can reference each other.</summary>
internal sealed class SigningKey
{
    public required string Kid { get; init; }
    public required ECDsa Key { get; init; }
    public required byte[] Spki { get; init; }

    public static SigningKey From(ECDsa key)
    {
        var spki = key.ExportSubjectPublicKeyInfo();
        return new SigningKey { Kid = MakeKid(spki), Key = key, Spki = spki };
    }

    private static string MakeKid(byte[] spki)
    {
        var hash = SHA256.HashData(spki);
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')[..16];
    }
}
