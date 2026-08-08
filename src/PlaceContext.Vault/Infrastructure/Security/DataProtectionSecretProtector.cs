using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using PlaceContext.Application.Ports;

namespace PlaceContext.Vault.Infrastructure.Security;

/// <summary>Vault secrets: thin façade over <see cref="IDataEncryptor"/> with the vault purpose.</summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private const string Prefix = "pcenc1.";
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector(
            "placecontext.data." + DataEncryptionPurpose.Vault);

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext) || plaintext.StartsWith(Prefix, StringComparison.Ordinal))
            return plaintext;

        return Prefix + _protector.Protect(plaintext);
    }

    public string Unprotect(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return string.Empty;

        try
        {
            return ciphertext.StartsWith(Prefix, StringComparison.Ordinal)
                ? _protector.Unprotect(ciphertext[Prefix.Length..])
                : _protector.Unprotect(ciphertext);
        }
        catch (CryptographicException)
        {
            // Unprefixed values can be historical plaintext; preserve them until the optional
            // Vault encryption bootstrap rewrites them.
            return ciphertext.StartsWith(Prefix, StringComparison.Ordinal) ? string.Empty : ciphertext;
        }
    }
}
