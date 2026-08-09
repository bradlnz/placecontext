namespace PlaceContext.Application.Ports;

/// <summary>
/// Mints short-lived Tailscale auth keys via the tailnet's OAuth API, so a portal action can hand a
/// brand-new agent machine a one-time key without ever storing a long-lived auth key anywhere.
/// Stateless — the OAuth client credentials are supplied per call (read from the vault by the caller;
/// see <c>SystemProjects.Cluster</c>).
/// </summary>
public interface ITailscaleKeyMinter
{
    /// <summary>
    /// Exchanges the OAuth client credentials for an access token, then requests a single-use,
    /// ephemeral, pre-authorized device key tagged with <paramref name="tags"/> (comma/space
    /// separated ACL tags, e.g. <c>"tag:agent"</c>). Returns the <c>tskey-auth-…</c> string, or null
    /// if the credentials are missing/invalid or the API call fails.
    /// </summary>
    Task<string?> MintEphemeralAgentKeyAsync(string clientId, string clientSecret, string tags, CancellationToken ct = default);
}
