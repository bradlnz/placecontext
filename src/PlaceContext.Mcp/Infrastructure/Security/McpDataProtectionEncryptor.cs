using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using PlaceContext.Application.Ports;

namespace PlaceContext.Mcp.Infrastructure.Security;

public sealed class McpDataProtectionEncryptor(IDataProtectionProvider provider) : IDataEncryptor
{
    private const string Prefix = "pcenc1.";

    public bool IsProtected(string? value)
        => !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string? plaintext, string purpose)
        => string.IsNullOrEmpty(plaintext) || IsProtected(plaintext)
            ? plaintext ?? string.Empty
            : Prefix + Protector(purpose).Protect(plaintext);

    public string Unprotect(string? ciphertext, string purpose)
    {
        if (string.IsNullOrEmpty(ciphertext) || !IsProtected(ciphertext))
            return ciphertext ?? string.Empty;
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
        return Encoding.UTF8.GetBytes(
            Prefix + Protector(purpose).Protect(Convert.ToBase64String(plaintext)));
    }

    public byte[] UnprotectBytes(byte[] ciphertext, string purpose)
    {
        if (ciphertext.Length == 0)
            return ciphertext;
        var encoded = Encoding.UTF8.GetString(ciphertext);
        if (!encoded.StartsWith(Prefix, StringComparison.Ordinal))
            return ciphertext;
        try
        {
            return Convert.FromBase64String(
                Protector(purpose).Unprotect(encoded[Prefix.Length..]));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return [];
        }
    }

    private IDataProtector Protector(string purpose)
        => provider.CreateProtector("placecontext.data." + purpose);
}
