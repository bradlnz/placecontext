using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using PlaceContext.Application.Ports;

namespace PlaceContext.Crm.Infrastructure.Security;

/// <summary>CRM runtime adapter for the shared data-at-rest contract.</summary>
public sealed class CrmDataProtectionEncryptor : IDataEncryptor
{
    public const string Prefix = "pcenc1.";
    public const int MaxPlaintextChars = 16 * 1024 * 1024;
    public const int MaxPlaintextBytes = 32 * 1024 * 1024;

    private readonly IDataProtectionProvider _provider;

    public CrmDataProtectionEncryptor(IDataProtectionProvider provider) => _provider = provider;

    public bool IsProtected(string? value)
        => !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string? plaintext, string purpose)
    {
        if (string.IsNullOrEmpty(plaintext) || IsProtected(plaintext))
            return plaintext ?? string.Empty;
        if (plaintext.Length > MaxPlaintextChars)
            throw new InvalidOperationException(
                $"Refusing to encrypt payload larger than {MaxPlaintextChars:N0} characters "
                + $"(got {plaintext.Length:N0}).");

        return Prefix + Protector(purpose).Protect(plaintext);
    }

    public string Unprotect(string? ciphertext, string purpose)
    {
        if (string.IsNullOrEmpty(ciphertext) || !IsProtected(ciphertext))
            return ciphertext ?? string.Empty;
        if (ciphertext.Length > MaxPlaintextChars * 2)
            return string.Empty;

        try
        {
            return Protector(purpose).Unprotect(ciphertext[Prefix.Length..]);
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
    }

    public byte[] ProtectBytes(byte[] plaintext, string purpose)
    {
        if (plaintext.Length == 0)
            return plaintext;
        if (plaintext.Length > MaxPlaintextBytes)
            throw new InvalidOperationException(
                $"Refusing to encrypt binary larger than {MaxPlaintextBytes:N0} bytes "
                + $"(got {plaintext.Length:N0}).");

        var protectedPayload = Encoding.UTF8.GetBytes(
            Protector(purpose).Protect(Convert.ToBase64String(plaintext)));
        var marker = Encoding.ASCII.GetBytes(Prefix);
        var result = new byte[marker.Length + protectedPayload.Length];
        Buffer.BlockCopy(marker, 0, result, 0, marker.Length);
        Buffer.BlockCopy(protectedPayload, 0, result, marker.Length, protectedPayload.Length);
        return result;
    }

    public byte[] UnprotectBytes(byte[] ciphertext, string purpose)
    {
        if (ciphertext.Length == 0)
            return ciphertext;
        if (ciphertext.Length > MaxPlaintextBytes * 2)
            return [];

        var marker = Encoding.ASCII.GetBytes(Prefix);
        if (ciphertext.Length < marker.Length
            || !ciphertext.AsSpan(0, marker.Length).SequenceEqual(marker))
            return ciphertext;

        try
        {
            var encoded = Protector(purpose).Unprotect(
                Encoding.UTF8.GetString(ciphertext, marker.Length, ciphertext.Length - marker.Length));
            var plaintext = Convert.FromBase64String(encoded);
            return plaintext.Length > MaxPlaintextBytes ? [] : plaintext;
        }
        catch (CryptographicException)
        {
            return [];
        }
        catch (FormatException)
        {
            return [];
        }
    }

    private IDataProtector Protector(string purpose)
        => _provider.CreateProtector("placecontext.data." + purpose);
}
