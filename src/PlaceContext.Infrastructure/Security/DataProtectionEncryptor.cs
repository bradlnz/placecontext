using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Security;

/// <summary>
/// AES field encryption via ASP.NET Data Protection, purpose-scoped. Wire format:
/// <c>pcenc1.</c> + base64url(DP payload). A DB dump without the DP key ring (and envelope key)
/// cannot recover plaintext.
/// Hard caps on input size defend against memory-exhaustion from oversized payloads.
/// </summary>
public sealed class DataProtectionEncryptor : IDataEncryptor
{
    public const string Prefix = "pcenc1.";

    /// <summary>Max characters accepted for string Protect (16 MiB).</summary>
    public const int MaxPlaintextChars = 16 * 1024 * 1024;

    /// <summary>Max bytes accepted for ProtectBytes (32 MiB).</summary>
    public const int MaxPlaintextBytes = 32 * 1024 * 1024;

    private readonly IDataProtectionProvider _provider;

    public DataProtectionEncryptor(IDataProtectionProvider provider) => _provider = provider;

    public bool IsProtected(string? value)
        => !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string? plaintext, string purpose)
    {
        if (string.IsNullOrEmpty(plaintext) || IsProtected(plaintext))
            return plaintext ?? "";
        if (plaintext.Length > MaxPlaintextChars)
            throw new InvalidOperationException(
                $"Refusing to encrypt payload larger than {MaxPlaintextChars:N0} characters (got {plaintext.Length:N0}).");
        return Prefix + Protector(purpose).Protect(plaintext);
    }

    public string Unprotect(string? ciphertext, string purpose)
    {
        if (string.IsNullOrEmpty(ciphertext) || !IsProtected(ciphertext))
            return ciphertext ?? "";
        if (ciphertext.Length > MaxPlaintextChars * 2)
            return ""; // refuse pathological ciphertext
        try
        {
            return Protector(purpose).Unprotect(ciphertext[Prefix.Length..]);
        }
        catch (CryptographicException)
        {
            // Wrong purpose / corrupted / key rotated without migration — surface empty rather than crash reads.
            return "";
        }
    }

    public byte[] ProtectBytes(byte[] plaintext, string purpose)
    {
        if (plaintext.Length == 0) return plaintext;
        if (plaintext.Length > MaxPlaintextBytes)
            throw new InvalidOperationException(
                $"Refusing to encrypt binary larger than {MaxPlaintextBytes:N0} bytes (got {plaintext.Length:N0}).");
        // Prefix as ASCII so we can detect encrypted blobs without decrypting.
        var protectedPayload = Encoding.UTF8.GetBytes(Protector(purpose).Protect(Convert.ToBase64String(plaintext)));
        var marker = Encoding.ASCII.GetBytes(Prefix);
        var result = new byte[marker.Length + protectedPayload.Length];
        Buffer.BlockCopy(marker, 0, result, 0, marker.Length);
        Buffer.BlockCopy(protectedPayload, 0, result, marker.Length, protectedPayload.Length);
        return result;
    }

    public byte[] UnprotectBytes(byte[] ciphertext, string purpose)
    {
        if (ciphertext.Length == 0) return ciphertext;
        if (ciphertext.Length > MaxPlaintextBytes * 2)
            return Array.Empty<byte>();
        var marker = Encoding.ASCII.GetBytes(Prefix);
        if (ciphertext.Length < marker.Length || !ciphertext.AsSpan(0, marker.Length).SequenceEqual(marker))
            return ciphertext; // legacy plaintext object
        try
        {
            var b64 = Protector(purpose).Unprotect(Encoding.UTF8.GetString(ciphertext, marker.Length, ciphertext.Length - marker.Length));
            var plain = Convert.FromBase64String(b64);
            // Guard against base64 decoding tricks that expand beyond the hard cap.
            if (plain.Length > MaxPlaintextBytes) return Array.Empty<byte>();
            return plain;
        }
        catch (CryptographicException)
        {
            return Array.Empty<byte>();
        }
        catch (FormatException)
        {
            return Array.Empty<byte>();
        }
    }

    private IDataProtector Protector(string purpose)
        => _provider.CreateProtector("placecontext.data." + purpose);
}
