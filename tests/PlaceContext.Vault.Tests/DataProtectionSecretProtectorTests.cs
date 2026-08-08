using Microsoft.AspNetCore.DataProtection;
using PlaceContext.Vault.Infrastructure.Security;

namespace PlaceContext.Vault.Tests;

public sealed class DataProtectionSecretProtectorTests
{
    [Fact]
    public void Protect_ValidPlaintext_UsesCompatibleWireFormatAndRoundTrips()
    {
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());

        var ciphertext = protector.Protect("top-secret");

        Assert.StartsWith("pcenc1.", ciphertext, StringComparison.Ordinal);
        Assert.NotEqual("top-secret", ciphertext);
        Assert.Equal("top-secret", protector.Unprotect(ciphertext));
        Assert.Equal(ciphertext, protector.Protect(ciphertext));
    }

    [Fact]
    public void Unprotect_LegacyPlaintext_PreservesValueForBootstrapMigration()
    {
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());

        Assert.Equal("legacy-value", protector.Unprotect("legacy-value"));
        Assert.Equal(string.Empty, protector.Unprotect("pcenc1.not-valid"));
    }
}
