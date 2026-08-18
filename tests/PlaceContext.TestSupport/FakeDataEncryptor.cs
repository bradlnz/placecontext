using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

/// <summary>No-op encryptor for tests — stores values as plaintext (no encryption).</summary>
public sealed class FakeDataEncryptor : IDataEncryptor
{
    public bool IsProtected(string? value) => false;
    public string Protect(string? plaintext, string purpose) => plaintext ?? "";
    public string Unprotect(string? ciphertext, string purpose) => ciphertext ?? "";
    public byte[] ProtectBytes(byte[] plaintext, string purpose) => plaintext;
    public byte[] UnprotectBytes(byte[] ciphertext, string purpose) => ciphertext;
}
