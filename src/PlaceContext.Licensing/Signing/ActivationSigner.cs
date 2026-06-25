using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlaceContext.Licensing.Models;

namespace PlaceContext.Licensing.Signing;

/// <summary>
/// Mints and rotates self-host activation tokens. A token is <c>base64url(payloadJson).base64url(ECDSA
/// P-256/SHA-256 DER signature)</c> — the same format the deployment verifies offline, so the licensor and
/// the offline <c>tools/activation.sh</c> path are interoperable.
///
/// Rotation: the signer holds an ordered list of keys (oldest = root, newest = active signer). The root key
/// is the trust anchor a deployment pins. <see cref="Rotate"/> appends a fresh active key; <see cref="KeySet"/>
/// publishes every key, each non-root one ENDORSED by its predecessor (a signature over the new key's SPKI),
/// so a client anchored on the root can extend trust forward through the chain without a config change.
/// </summary>
public sealed class ActivationSigner : IDisposable
{
    private static readonly JsonSerializerOptions TokenJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly List<SigningKey> _keys = new();
    private readonly object _gate = new();

    /// <summary>Builds a signer rooted at <paramref name="rootKeyPem"/> (an EC private key PEM); when null/blank
    /// a fresh P-256 root key is generated (its public key should then be pinned by deployments).</summary>
    public ActivationSigner(string? rootKeyPem = null)
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (!string.IsNullOrWhiteSpace(rootKeyPem))
            ecdsa.ImportFromPem(rootKeyPem);
        _keys.Add(SigningKey.From(ecdsa));
    }

    /// <summary>The root (anchor) public key as base64 SubjectPublicKeyInfo — paste into a deployment's
    /// <c>PlaceContext:Activation:PublicKey</c>.</summary>
    public string RootPublicKeyBase64
    {
        get { lock (_gate) return Convert.ToBase64String(_keys[0].Spki); }
    }

    /// <summary>The kid of the key currently signing tokens (the newest in the chain).</summary>
    public string ActiveKid
    {
        get { lock (_gate) return _keys[^1].Kid; }
    }

    /// <summary>Signs an entitlement token for the given claims with the active key.</summary>
    public string SignToken(string? org, string? plan, DateTimeOffset? expiresAt)
    {
        var claims = new TokenClaims { Org = org, Plan = plan, Exp = expiresAt?.ToUnixTimeSeconds() };
        var payload = JsonSerializer.SerializeToUtf8Bytes(claims, TokenJson);

        SigningKey active;
        lock (_gate) active = _keys[^1];

        var signature = active.Key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return $"{B64Url(payload)}.{B64Url(signature)}";
    }

    /// <summary>The published rotation key set: the root, then each rotated-in key endorsed by its predecessor.</summary>
    public IReadOnlyList<PublicKeyEntry> KeySet()
    {
        lock (_gate)
        {
            var result = new List<PublicKeyEntry>(_keys.Count);
            for (var i = 0; i < _keys.Count; i++)
            {
                var key = _keys[i];
                if (i == 0)
                {
                    result.Add(new PublicKeyEntry(key.Kid, Convert.ToBase64String(key.Spki), null, null));
                    continue;
                }

                var endorser = _keys[i - 1];
                var endorsement = endorser.Key.SignData(
                    key.Spki, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
                result.Add(new PublicKeyEntry(
                    key.Kid,
                    Convert.ToBase64String(key.Spki),
                    endorser.Kid,
                    Convert.ToBase64String(endorsement)));
            }
            return result;
        }
    }

    /// <summary>Generates a fresh active key endorsed by the current one and returns its kid.</summary>
    public string Rotate()
    {
        lock (_gate)
        {
            var key = SigningKey.From(ECDsa.Create(ECCurve.NamedCurves.nistP256));
            _keys.Add(key);
            return key.Kid;
        }
    }

    public void Dispose()
    {
        lock (_gate)
            foreach (var key in _keys)
                key.Key.Dispose();
    }

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class TokenClaims
    {
        [JsonPropertyName("org")] public string? Org { get; set; }
        [JsonPropertyName("plan")] public string? Plan { get; set; }
        [JsonPropertyName("exp")] public long? Exp { get; set; }
    }
}
