namespace PlaceContext.Application.Ports;

/// <summary>
/// Encrypts/decrypts Vault secret values for storage. Implemented with ASP.NET Data Protection
/// (AES) over the shared, DB-persisted key ring, so values are encrypted at rest and any replica
/// can decrypt them. Plaintext never leaves the Host (decrypted only to inject into a job sandbox).
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
