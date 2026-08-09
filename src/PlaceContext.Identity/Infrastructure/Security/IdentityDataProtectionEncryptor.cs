using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using PlaceContext.Application.Ports;

namespace PlaceContext.Identity.Infrastructure.Security;

public sealed class IdentityDataProtectionEncryptor(IDataProtectionProvider provider) : IDataEncryptor
{
    private const string Prefix = "pcenc1.";
    private const int MaxPlaintextChars = 16 * 1024 * 1024;
    private const int MaxPlaintextBytes = 32 * 1024 * 1024;

    public bool IsProtected(string? value)
        => !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string? plaintext, string purpose)
    {
        if (string.IsNullOrEmpty(plaintext) || IsProtected(plaintext)) return plaintext ?? string.Empty;
        if (plaintext.Length > MaxPlaintextChars) throw new InvalidOperationException("Payload is too large to encrypt.");
        return Prefix + Protector(purpose).Protect(plaintext);
    }

    public string Unprotect(string? ciphertext, string purpose)
    {
        if (string.IsNullOrEmpty(ciphertext) || !IsProtected(ciphertext)) return ciphertext ?? string.Empty;
        if (ciphertext.Length > MaxPlaintextChars * 2) return string.Empty;
        try { return Protector(purpose).Unprotect(ciphertext[Prefix.Length..]); }
        catch (CryptographicException) { return string.Empty; }
    }

    public byte[] ProtectBytes(byte[] plaintext, string purpose)
    {
        if (plaintext.Length == 0) return plaintext;
        if (plaintext.Length > MaxPlaintextBytes) throw new InvalidOperationException("Payload is too large to encrypt.");
        return Encoding.ASCII.GetBytes(Prefix).Concat(
            Encoding.UTF8.GetBytes(Protector(purpose).Protect(Convert.ToBase64String(plaintext)))).ToArray();
    }

    public byte[] UnprotectBytes(byte[] ciphertext, string purpose)
    {
        if (ciphertext.Length == 0) return ciphertext;
        var marker = Encoding.ASCII.GetBytes(Prefix);
        if (ciphertext.Length < marker.Length || !ciphertext.AsSpan(0, marker.Length).SequenceEqual(marker)) return ciphertext;
        try
        {
            var protectedText = Encoding.UTF8.GetString(ciphertext, marker.Length, ciphertext.Length - marker.Length);
            var plaintext = Convert.FromBase64String(Protector(purpose).Unprotect(protectedText));
            return plaintext.Length <= MaxPlaintextBytes ? plaintext : Array.Empty<byte>();
        }
        catch (CryptographicException) { return Array.Empty<byte>(); }
        catch (FormatException) { return Array.Empty<byte>(); }
    }

    private IDataProtector Protector(string purpose)
        => provider.CreateProtector("placecontext.data." + purpose);
}
