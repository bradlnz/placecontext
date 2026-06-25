using System.Security.Cryptography;
using System.Text.Json;
using PlaceContext.Licensing.Signing;

namespace PlaceContext.Licensing.Tests;

/// <summary>
/// Tests the licensor signer: it mints tokens in the offline-verifiable format, and its rotation key set
/// chains each new key back to the trust anchor via an endorsement signature.
/// </summary>
public class ActivationSignerTests
{
    [Fact]
    public void Signed_token_verifies_under_the_root_public_key_and_carries_claims()
    {
        using var signer = new ActivationSigner();
        var token = signer.SignToken("Acme Inc", "pro", DateTimeOffset.UtcNow.AddDays(30));

        var (payload, signature) = SplitToken(token);
        Assert.True(VerifyUnder(signer.RootPublicKeyBase64, payload, signature));

        using var doc = JsonDocument.Parse(payload);
        Assert.Equal("Acme Inc", doc.RootElement.GetProperty("org").GetString());
        Assert.Equal("pro", doc.RootElement.GetProperty("plan").GetString());
    }

    [Fact]
    public void Perpetual_token_omits_exp()
    {
        using var signer = new ActivationSigner();
        var (payload, _) = SplitToken(signer.SignToken("Acme", null, null));
        using var doc = JsonDocument.Parse(payload);
        Assert.False(doc.RootElement.TryGetProperty("exp", out _));
    }

    [Fact]
    public void Rotation_publishes_a_new_active_key_endorsed_by_its_predecessor()
    {
        using var signer = new ActivationSigner();
        var rootKid = signer.ActiveKid;

        var newKid = signer.Rotate();
        Assert.Equal(newKid, signer.ActiveKid);

        var keys = signer.KeySet();
        Assert.Equal(2, keys.Count);

        var root = keys[0];
        var rotated = keys[1];
        Assert.Equal(rootKid, root.Kid);
        Assert.Null(root.EndorsedBy);
        Assert.Equal(rootKid, rotated.EndorsedBy);

        // The endorsement is a signature, by the root key, over the rotated key's SPKI bytes.
        var rotatedSpki = Convert.FromBase64String(rotated.Spki);
        var endorsement = Convert.FromBase64String(rotated.Endorsement!);
        Assert.True(VerifyRaw(root.Spki, rotatedSpki, endorsement));

        // A token minted after rotation verifies under the new active key (not the root).
        var (payload, signature) = SplitToken(signer.SignToken("Acme", "pro", null));
        Assert.True(VerifyUnder(rotated.Spki, payload, signature));
    }

    private static (byte[] payload, byte[] signature) SplitToken(string token)
    {
        var parts = token.Split('.');
        return (B64UrlDecode(parts[0]), B64UrlDecode(parts[1]));
    }

    private static bool VerifyUnder(string spkiB64, byte[] data, byte[] signature)
        => VerifyRaw(spkiB64, data, signature);

    private static bool VerifyRaw(string spkiB64, byte[] data, byte[] signature)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(spkiB64), out _);
        return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    private static byte[] B64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
