using System.Net;
using System.Net.Http.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;

namespace PlaceContext.Infrastructure.Activation;

/// <summary>
/// Online activation: the deployment phones home to the configured PlaceContext licensing server, which
/// handles activation, payments, and signing-key rotation. On each refresh it POSTs the license key to
/// <c>{ServerUrl}/activate</c>, receives a freshly-signed entitlement token plus the current rotation key
/// set, verifies the token offline against its trusted keys (anchored by the configured public key), and
/// caches the result. Refreshes run on a background timer (<see cref="RemoteActivationService"/> is a
/// singleton; <see cref="ActivationRefreshService"/> drives it).
///
/// Resilience (so a self-host box is not knocked offline by a transient server blip):
///  • A locally-configured signed token is self-verifying and always wins, server reachable or not.
///  • Otherwise the last successful activation is cached and kept valid through a configurable GRACE window
///    when the server is unreachable; past the window it degrades to not-active.
///  • An explicit server denial (payment past due / unknown / revoked key) is honoured immediately — grace
///    only covers unreachability, never a "no".
///
/// Config (<c>PlaceContext:Activation</c>): <c>ServerUrl</c>, <c>Key</c>, <c>PublicKey</c> (anchor SPKI),
/// <c>Enforce</c>, <c>GraceDays</c> (default 7), <c>RefreshMinutes</c> (default 60), <c>InstanceId</c>.
/// </summary>
public sealed class RemoteActivationService : IActivationService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IClock _clock;

    private readonly string? _serverUrl;
    private readonly string? _key;
    private readonly byte[]? _anchor;
    private readonly string _instanceId;
    private readonly TimeSpan _grace;

    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _trusted = new(); // kid -> SubjectPublicKeyInfo (rotated keys)
    private ActivationInfo _cached;
    private ActivationInfo? _lastGood;
    private DateTimeOffset _lastGoodAt;

    public RemoteActivationService(IHttpClientFactory httpFactory, IConfiguration config, IClock clock)
    {
        _httpFactory = httpFactory;
        _clock = clock;

        var section = config.GetSection("PlaceContext:Activation");
        _serverUrl = section["ServerUrl"];
        _key = section["Key"];
        Enforced = bool.TryParse(section["Enforce"], out var e) && e;
        _instanceId = string.IsNullOrWhiteSpace(section["InstanceId"]) ? Environment.MachineName : section["InstanceId"]!;

        var graceDays = double.TryParse(section["GraceDays"], out var g) ? g : 7d;
        _grace = TimeSpan.FromDays(graceDays <= 0 ? 0 : graceDays);
        var refreshMinutes = double.TryParse(section["RefreshMinutes"], out var r) && r > 0 ? r : 60d;
        RefreshInterval = TimeSpan.FromMinutes(refreshMinutes);

        var anchorB64 = section["PublicKey"];
        if (!string.IsNullOrWhiteSpace(anchorB64))
        {
            try { _anchor = Convert.FromBase64String(anchorB64); }
            catch { _anchor = null; }
        }

        // Seed before the first refresh: a configured key that is itself a signed token verifies offline
        // immediately; otherwise present a calm "pending" state until the background refresh reaches the server.
        var seed = ActivationTokenVerifier.Verify(_key, TrustedSpki(), _clock);
        if (seed.IsActive)
        {
            _cached = seed;
            _lastGood = seed;
            _lastGoodAt = _clock.UtcNow;
        }
        else
        {
            _cached = ActivationInfo.Missing() with
            {
                Reason = string.IsNullOrWhiteSpace(_key)
                    ? "No activation key configured."
                    : "Activation pending — contacting the licensing server…"
            };
        }
    }

    public bool Enforced { get; }

    /// <summary>How often <see cref="ActivationRefreshService"/> calls <see cref="RefreshAsync"/>.</summary>
    public TimeSpan RefreshInterval { get; }

    public ActivationInfo Current
    {
        get { lock (_gate) return _cached; }
    }

    /// <summary>
    /// Contacts the licensing server once and updates the cached state. Never throws for an expected outcome
    /// (network failure, server error, denial) — those are folded into <see cref="Current"/>.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_serverUrl))
            return; // Registered only when a server URL is configured; nothing to do otherwise.
        if (string.IsNullOrWhiteSpace(_key))
        {
            Set(ActivationInfo.Missing());
            return;
        }

        try
        {
            var client = _httpFactory.CreateClient("activation");
            var request = new ActivateRequest(_key!, _instanceId);
            using var response = await client.PostAsJsonAsync(Combine(_serverUrl!, "activate"), request, ct);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<ActivateResponse>(cancellationToken: ct);
                if (body is null || string.IsNullOrWhiteSpace(body.Token))
                {
                    Set(ActivationInfo.Invalid("Licensing server returned an empty activation token."));
                    return;
                }

                IngestRotationKeys(body.Keys);
                var info = ActivationTokenVerifier.Verify(body.Token, TrustedSpki(), _clock);
                lock (_gate)
                {
                    _cached = info;
                    if (info.IsActive)
                    {
                        _lastGood = info;
                        _lastGoodAt = _clock.UtcNow;
                    }
                }
                return;
            }

            // The server was reached and explicitly declined — honour it (no grace for a "no").
            if (response.StatusCode == HttpStatusCode.PaymentRequired)
            {
                Set(ActivationInfo.Invalid(await DenialReason(response, ct, "Subscription payment is past due.")));
                return;
            }
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
            {
                Set(ActivationInfo.Invalid(await DenialReason(response, ct, "The licensing server rejected this activation key.")));
                return;
            }

            // A 5xx / unexpected status is treated as "server unavailable" → grace path.
            ApplyOfflineOrGrace($"licensing server returned {(int)response.StatusCode} {response.StatusCode}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // shutdown — let the host stop us
        }
        catch (Exception ex)
        {
            // Network failure / timeout / DNS — the server is unreachable. Stay up within the grace window.
            ApplyOfflineOrGrace(ex.Message);
        }
    }

    /// <summary>Server unreachable: prefer a self-verifying offline token, else cached last-good within grace.</summary>
    private void ApplyOfflineOrGrace(string detail)
    {
        // 1) A locally-configured signed token is cryptographically valid on its own — independent of the server.
        var offline = ActivationTokenVerifier.Verify(_key, TrustedSpki(), _clock);
        if (offline.IsActive)
        {
            Set(offline with { Reason = $"Activated (offline token; licensing server unreachable: {detail})." });
            return;
        }

        lock (_gate)
        {
            if (_lastGood is { } good && good.IsActive)
            {
                var graceEnds = _lastGoodAt + _grace;
                if (_clock.UtcNow <= graceEnds)
                {
                    _cached = good with
                    {
                        Reason = $"Licensing server unreachable; running on cached activation " +
                                 $"(grace until {graceEnds:yyyy-MM-dd HH:mm} UTC). Detail: {detail}"
                    };
                    return;
                }

                _cached = ActivationInfo.Expired(good.Organisation, good.Plan, graceEnds) with
                {
                    Reason = $"Licensing server unreachable and the {_grace.TotalDays:0}-day grace period " +
                             $"elapsed on {graceEnds:yyyy-MM-dd}. Last error: {detail}"
                };
                return;
            }

            _cached = ActivationInfo.Invalid($"Could not reach the licensing server to activate: {detail}");
        }
    }

    /// <summary>
    /// Extend the trusted-key set from the server's rotation set. The configured anchor is the root of trust;
    /// a served key is admitted when its SPKI equals the anchor, or when its endorsement verifies under a key
    /// already trusted (a rotation chain). Iterated to a fixpoint so multi-step chains resolve in any order.
    /// </summary>
    private void IngestRotationKeys(IReadOnlyList<PublicKeyEntry>? keys)
    {
        if (keys is null || keys.Count == 0)
            return;

        lock (_gate)
        {
            var pending = new List<PublicKeyEntry>(keys);
            bool added = true;
            while (added)
            {
                added = false;
                for (var i = pending.Count - 1; i >= 0; i--)
                {
                    var entry = pending[i];
                    byte[] spki;
                    try { spki = Convert.FromBase64String(entry.Spki); }
                    catch { pending.RemoveAt(i); continue; }

                    // Root of trust: the served key IS the configured anchor.
                    if (_anchor is not null && spki.AsSpan().SequenceEqual(_anchor))
                    {
                        _trusted[entry.Kid] = spki;
                        pending.RemoveAt(i);
                        added = true;
                        continue;
                    }

                    // Rotation: admit a key endorsed (signed) by a key we already trust.
                    if (entry.EndorsedByKid is { } byKid
                        && entry.Endorsement is { } endorsementB64
                        && _trusted.TryGetValue(byKid, out var endorserSpki))
                    {
                        byte[] endorsement;
                        try { endorsement = Convert.FromBase64String(endorsementB64); }
                        catch { pending.RemoveAt(i); continue; }

                        if (ActivationTokenVerifier.VerifiesUnderAny(spki, endorsement, new[] { endorserSpki }))
                        {
                            _trusted[entry.Kid] = spki;
                            pending.RemoveAt(i);
                            added = true;
                        }
                    }
                }
            }
            // Anything still pending could not be chained back to the anchor — deliberately not trusted.
        }
    }

    /// <summary>The current trusted SPKI set: the configured anchor plus every rotated-in key.</summary>
    private IReadOnlyCollection<byte[]> TrustedSpki()
    {
        lock (_gate)
        {
            var all = new List<byte[]>(_trusted.Count + 1);
            if (_anchor is not null)
                all.Add(_anchor);
            all.AddRange(_trusted.Values);
            return all;
        }
    }

    private void Set(ActivationInfo info)
    {
        lock (_gate) _cached = info;
    }

    private static async Task<string> DenialReason(HttpResponseMessage response, CancellationToken ct, string fallback)
    {
        try
        {
            var body = (await response.Content.ReadAsStringAsync(ct)).Trim();
            if (!string.IsNullOrEmpty(body))
                return body.Length > 300 ? body[..300] : body;
        }
        catch
        {
            // Ignore — fall back to the generic reason below.
        }
        return fallback;
    }

    private static string Combine(string baseUrl, string path) =>
        $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
}
