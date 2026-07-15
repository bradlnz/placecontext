using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Security;

/// <summary>Vault secrets: thin façade over <see cref="IDataEncryptor"/> with the vault purpose.</summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataEncryptor _enc;
    public DataProtectionSecretProtector(IDataEncryptor enc) => _enc = enc;

    public string Protect(string plaintext)
        => _enc.Protect(plaintext, IDataEncryptor.Purpose.Vault);

    public string Unprotect(string ciphertext)
        => _enc.Unprotect(ciphertext, IDataEncryptor.Purpose.Vault);
}
