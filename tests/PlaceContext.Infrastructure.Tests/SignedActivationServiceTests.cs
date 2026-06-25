using System.Security.Cryptography;
using System.Text;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Activation;
using PlaceContext.TestSupport;
using Microsoft.Extensions.Configuration;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// Tests offline activation-key validation: a key is base64url(payload).base64url(ECDSA-P256/SHA256-DER)
/// over the payload JSON. Keys are minted in-process here; a final (skippable) test proves interop with
/// keys minted by the openssl tools/activation.sh script.
/// </summary>
public class SignedActivationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    private static SignedActivationService Build(string? key, string? publicKeyB64, bool enforce = false)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlaceContext:Activation:Key"] = key,
            ["PlaceContext:Activation:PublicKey"] = publicKeyB64,
            ["PlaceContext:Activation:Enforce"] = enforce ? "true" : "false",
        }).Build();
        return new SignedActivationService(config, new FakeClock(Now));
    }

    private static (string key, string publicKeyB64) Mint(string payloadJson)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = Encoding.UTF8.GetBytes(payloadJson);
        var sig = ecdsa.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        var key = $"{B64Url(payload)}.{B64Url(sig)}";
        var pub = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        return (key, pub);
    }

    private static string B64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void Valid_unexpired_key_is_active_with_claims()
    {
        var (key, pub) = Mint("{\"org\":\"Acme Inc\",\"plan\":\"pro\",\"exp\":1798761600}"); // 2027
        var info = Build(key, pub).Current;

        Assert.Equal(ActivationStatus.Active, info.Status);
        Assert.True(info.IsActive);
        Assert.Equal("Acme Inc", info.Organisation);
        Assert.Equal("pro", info.Plan);
    }

    [Fact]
    public void Perpetual_key_without_exp_is_active()
    {
        var (key, pub) = Mint("{\"org\":\"Acme\"}");
        var info = Build(key, pub).Current;
        Assert.Equal(ActivationStatus.Active, info.Status);
        Assert.Null(info.ExpiresAt);
    }

    [Fact]
    public void Expired_key_is_expired()
    {
        var (key, pub) = Mint("{\"org\":\"Acme\",\"exp\":1577836800}"); // 2020
        var info = Build(key, pub).Current;
        Assert.Equal(ActivationStatus.Expired, info.Status);
        Assert.False(info.IsActive);
    }

    [Fact]
    public void Tampered_payload_fails_signature()
    {
        var (key, pub) = Mint("{\"org\":\"Acme\",\"plan\":\"pro\"}");
        // Swap the payload for a different one while keeping the original signature.
        var forgedPayload = B64Url(Encoding.UTF8.GetBytes("{\"org\":\"Evil\",\"plan\":\"enterprise\"}"));
        var tampered = $"{forgedPayload}.{key.Split('.')[1]}";

        var info = Build(tampered, pub).Current;
        Assert.Equal(ActivationStatus.Invalid, info.Status);
    }

    [Fact]
    public void Key_signed_by_a_different_private_key_is_invalid()
    {
        var (key, _) = Mint("{\"org\":\"Acme\"}");
        var (_, otherPub) = Mint("{\"org\":\"Other\"}"); // a different keypair's public key
        var info = Build(key, otherPub).Current;
        Assert.Equal(ActivationStatus.Invalid, info.Status);
    }

    [Fact]
    public void Missing_key_is_missing()
        => Assert.Equal(ActivationStatus.Missing, Build(null, "anything").Current.Status);

    [Fact]
    public void Malformed_key_is_invalid()
        => Assert.Equal(ActivationStatus.Invalid, Build("not-a-valid-key", "x").Current.Status);

    [Fact]
    public void Enforced_flag_is_read_from_config()
        => Assert.True(Build(null, null, enforce: true).Enforced);

    // Interop proof: validates a key minted by tools/activation.sh against the committed public key.
    // Run locally with:
    //   PC_TEST_ACTIVATION_KEY=$(./tools/activation.sh sign --org Test --exp 2030-01-01) \
    //   PC_TEST_ACTIVATION_PUBKEY=$(./tools/activation.sh pubkey) dotnet test
    [Fact]
    public void Openssl_minted_key_interop()
    {
        var key = Environment.GetEnvironmentVariable("PC_TEST_ACTIVATION_KEY");
        var pub = Environment.GetEnvironmentVariable("PC_TEST_ACTIVATION_PUBKEY");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(pub))
            return; // skip when the openssl-minted key isn't provided

        Assert.Equal(ActivationStatus.Active, Build(key, pub).Current.Status);
    }
}
