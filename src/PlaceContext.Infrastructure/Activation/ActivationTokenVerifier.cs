using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Activation;

/// <summary>
/// Verifies a self-host activation token — <c>base64url(payloadJson).base64url(ECDSA P-256/SHA-256 DER
/// signature)</c> — against one or more trusted public keys, then maps the carried claims to an
/// <see cref="ActivationInfo"/>. Holding a SET of trusted keys (not just one) is what makes licensor key
/// rotation possible: during an overlap a token signed by the rotated-in key still verifies as long as
/// that key has entered the trusted set. BCL crypto only — no external dependency, no network. Shared by
/// the offline (<see cref="SignedActivationService"/>) and remote (<see cref="RemoteActivationService"/>)
/// activation services so the verification rules live in exactly one place.
/// </summary>
internal static class ActivationTokenVerifier
{
    /// <summary>Verifies <paramref name="token"/> against any of <paramref name="trustedSpki"/> and reads its claims.</summary>
    public static ActivationInfo Verify(string? token, IReadOnlyCollection<byte[]> trustedSpki, IClock clock)
    {
        if (string.IsNullOrWhiteSpace(token))
            return ActivationInfo.Missing();
        if (trustedSpki.Count == 0)
            return ActivationInfo.Invalid("No activation public key configured on this deployment.");

        var parts = token.Split('.');
        if (parts.Length != 2)
            return ActivationInfo.Invalid("Malformed activation key.");

        try
        {
            var payloadBytes = Base64UrlDecode(parts[0]);
            var signature = Base64UrlDecode(parts[1]);

            if (!VerifiesUnderAny(payloadBytes, signature, trustedSpki))
                return ActivationInfo.Invalid("Activation key signature did not verify.");

            var claims = JsonSerializer.Deserialize<Claims>(payloadBytes)
                ?? throw new InvalidOperationException("empty payload");

            DateTimeOffset? expiresAt = claims.Exp.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(claims.Exp.Value)
                : null;

            if (expiresAt is { } exp && exp < clock.UtcNow)
                return ActivationInfo.Expired(claims.Org, claims.Plan, exp);

            return ActivationInfo.Active(claims.Org, claims.Plan, expiresAt);
        }
        catch (Exception ex)
        {
            return ActivationInfo.Invalid($"Invalid activation key: {ex.Message}");
        }
    }

    /// <summary>
    /// True if <paramref name="signature"/> over <paramref name="data"/> verifies under ANY of the supplied
    /// SubjectPublicKeyInfo keys (ECDSA P-256 / SHA-256, DER). Used both for activation-token signatures and
    /// for the key-rotation endorsements that chain a rotated-in key back to a trusted one.
    /// </summary>
    public static bool VerifiesUnderAny(byte[] data, byte[] signature, IReadOnlyCollection<byte[]> trustedSpki)
    {
        foreach (var spki in trustedSpki)
        {
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
                if (ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                    return true;
            }
            catch
            {
                // A malformed key in the trusted set must not abort the whole check — try the next one.
            }
        }
        return false;
    }

    public static byte[] Base64UrlDecode(string value)
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
