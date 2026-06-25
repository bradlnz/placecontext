using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;

namespace PlaceContext.Infrastructure.Activation;

/// <summary>
/// Validates a self-host activation key entirely offline: the key is
/// <c>base64url(payload).base64url(signature)</c> where the signature is an ECDSA P-256 / SHA-256 (DER)
/// signature over the payload JSON, produced by the licensor's private key. This service verifies it
/// against the configured public key (BCL crypto only — no external dependency, no phone-home) and checks
/// the expiry claim. Result is computed once and cached. It is the default when no licensing-server URL is
/// configured; when one is, the deployment phones home instead via <see cref="RemoteActivationService"/>.
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

    public ActivationInfo Current => _cached ??= ActivationTokenVerifier.Verify(_key, TrustedKeys(), _clock);

    private IReadOnlyCollection<byte[]> TrustedKeys()
    {
        if (string.IsNullOrWhiteSpace(_publicKeyB64))
            return Array.Empty<byte[]>();
        try
        {
            return new[] { Convert.FromBase64String(_publicKeyB64) };
        }
        catch
        {
            // A malformed configured public key means nothing can be trusted — verifier reports "no key".
            return Array.Empty<byte[]>();
        }
    }
}
