namespace PlaceContext.Application.Ports;

/// <summary>
/// Application-level field encryption for data at rest. Ciphertext is only decryptable by Host
/// processes that hold the Data Protection key ring (and optional <c>PlaceContext:DataProtection:Key</c>
/// envelope). Raw Postgres / MinIO access therefore sees unreadable blobs; the portal and job pipeline
/// decrypt only in-process when serving UI or injecting into sandboxes.
/// </summary>
public interface IDataEncryptor
{
    /// <summary>True when <paramref name="value"/> is already in the protected wire format.</summary>
    bool IsProtected(string? value);

    /// <summary>Encrypt for storage. Empty/null and already-protected values pass through.</summary>
    string Protect(string? plaintext, string purpose);

    /// <summary>
    /// Decrypt for portal/job use. Legacy plaintext (no prefix) is returned unchanged so existing
    /// rows keep working until rewritten.
    /// </summary>
    string Unprotect(string? ciphertext, string purpose);

    byte[] ProtectBytes(byte[] plaintext, string purpose);
    byte[] UnprotectBytes(byte[] ciphertext, string purpose);
}
