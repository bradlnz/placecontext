using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;

namespace PlaceContext.Infrastructure.Activation;

/// <summary>
/// Validates a self-host activation key offline: the key is <c>base64url(payload).base64url(signature)</c>
/// where the signature is an ECDSA P-256 / SHA-256 (DER) signature over the payload JSON, produced by the
/// licensor's private key. This service verifies it against the configured public key (BCL crypto only —
/// no external dependency, no phone-home) and checks the expiry claim. Result is computed once and cached.
///
/// Config (<c>PlaceContext:Activation</c>): <c>Key</c> (the activation key), <c>PublicKey</c> (base64
/// SubjectPublicKeyInfo of the licensor's public key), <c>Enforce</c> (gate access when not active).
/// </summary>
public sealed class SignedActivationService : IActivationService
{
    private readonly string? _key;
    private readonly string? _publicKeyB64;
    private readonly IClock _clock;
    private ActivationInfo? _cached;

    public SignedActivationService(IConfiguration config, IClock clock)
    {
        var section = config.GetSection("PlaceContext:Activation");
        _key = section["Key"];
        _publicKeyB64 = section["PublicKey"];
        Enforced = bool.TryParse(section["Enforce"], out var e) && e;
        _clock = clock;
    }

    public bool Enforced { get; }

    public ActivationInfo Current => _cached ??= Validate();

    private ActivationInfo Validate()
    {
        if (string.IsNullOrWhiteSpace(_key))
            return ActivationInfo.Missing();
        if (string.IsNullOrWhiteSpace(_publicKeyB64))
            return ActivationInfo.Invalid("No activation public key configured on this deployment.");

        var parts = _key.Split('.');
        if (parts.Length != 2)
            return ActivationInfo.Invalid("Malformed activation key.");

        try
        {
            var payloadBytes = Base64UrlDecode(parts[0]);
            var signature = Base64UrlDecode(parts[1]);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(_publicKeyB64), out _);

            var verified = ecdsa.VerifyData(
                payloadBytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            if (!verified)
                return ActivationInfo.Invalid("Activation key signature did not verify.");

            var claims = JsonSerializer.Deserialize<Claims>(payloadBytes)
                ?? throw new InvalidOperationException("empty payload");

            DateTimeOffset? expiresAt = claims.Exp.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(claims.Exp.Value)
                : null;

            if (expiresAt is { } exp && exp < _clock.UtcNow)
                return ActivationInfo.Expired(claims.Org, claims.Plan, exp);

            return ActivationInfo.Active(claims.Org, claims.Plan, expiresAt);
        }
        catch (Exception ex)
        {
            return ActivationInfo.Invalid($"Invalid activation key: {ex.Message}");
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    private sealed class Claims
    {
        [JsonPropertyName("org")] public string? Org { get; set; }
        [JsonPropertyName("plan")] public string? Plan { get; set; }
        /// <summary>Expiry as Unix seconds (optional — omit for a perpetual key).</summary>
        [JsonPropertyName("exp")] public long? Exp { get; set; }
    }
}
