using Microsoft.AspNetCore.DataProtection;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Security;

/// <summary>Encrypts vault secrets with ASP.NET Data Protection (AES) over the shared, DB-persisted
/// key ring — encrypted at rest, decryptable by any replica.</summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _p;
    public DataProtectionSecretProtector(IDataProtectionProvider provider)
        => _p = provider.CreateProtector("placecontext.vault.secrets.v1");

    public string Protect(string plaintext) => _p.Protect(plaintext);
    public string Unprotect(string ciphertext) => _p.Unprotect(ciphertext);
}
