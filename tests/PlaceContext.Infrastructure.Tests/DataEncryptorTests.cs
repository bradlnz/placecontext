using Microsoft.AspNetCore.DataProtection;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Security;

namespace PlaceContext.Infrastructure.Tests;

public class DataEncryptorTests
{
    private static IDataEncryptor Create()
        => new DataProtectionEncryptor(new EphemeralDataProtectionProvider());

    [Fact]
    public void Round_trip_string()
    {
        var enc = Create();
        var cipher = enc.Protect("secret-job-source", IDataEncryptor.Purpose.JobSource);
        Assert.StartsWith(DataProtectionEncryptor.Prefix, cipher);
        Assert.NotEqual("secret-job-source", cipher);
        Assert.Equal("secret-job-source", enc.Unprotect(cipher, IDataEncryptor.Purpose.JobSource));
    }

    [Fact]
    public void Legacy_plaintext_unprotect_passthrough()
    {
        var enc = Create();
        Assert.Equal("old-plaintext", enc.Unprotect("old-plaintext", IDataEncryptor.Purpose.Context));
    }

    [Fact]
    public void Wrong_purpose_does_not_yield_plaintext()
    {
        var enc = Create();
        var cipher = enc.Protect("vault-secret", IDataEncryptor.Purpose.Vault);
        Assert.Equal("", enc.Unprotect(cipher, IDataEncryptor.Purpose.JobSource));
    }

    [Fact]
    public void Round_trip_bytes()
    {
        var enc = Create();
        var plain = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF
        var cipher = enc.ProtectBytes(plain, IDataEncryptor.Purpose.ObjectStore);
        Assert.NotEqual(plain, cipher);
        Assert.Equal(plain, enc.UnprotectBytes(cipher, IDataEncryptor.Purpose.ObjectStore));
    }

    [Fact]
    public void Vault_facade_uses_encryptor()
    {
        var enc = Create();
        var vault = new DataProtectionSecretProtector(enc);
        var c = vault.Protect("api-key-value");
        Assert.StartsWith(DataProtectionEncryptor.Prefix, c);
        Assert.Equal("api-key-value", vault.Unprotect(c));
    }

    [Fact]
    public void Protect_rejects_oversized_payload()
    {
        var enc = Create();
        var huge = new string('x', DataProtectionEncryptor.MaxPlaintextChars + 1);
        Assert.Throws<InvalidOperationException>(() => enc.Protect(huge, IDataEncryptor.Purpose.JobSource));
    }
}
