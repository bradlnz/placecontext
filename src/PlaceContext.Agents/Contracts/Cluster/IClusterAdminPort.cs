namespace PlaceContext.Application.Ports;

/// <summary>
/// Cluster fleet administration: designate the master used for join codes and multi-site ops, and
/// mint join material so a new machine (e.g. DigitalOcean) can reach the Mac/home master over Tailscale.
/// </summary>
public interface IClusterAdminPort
{
    Task<PromoteMasterResult> PromoteToMasterAsync(string nodeName, CancellationToken ct = default);

    /// <summary>Join material using this install's own seeded Tailscale auth key, if any.</summary>
    Task<ClusterJoinMaterial?> GetJoinMaterialAsync(CancellationToken ct = default);

    /// <summary>
    /// Join material with <paramref name="tailscaleAuthKeyOverride"/> embedded in place of any
    /// secret-seeded key — used when a fresh, single-use key was just minted for a one-time agent
    /// join (see <c>LaunchClusterAgentCommand</c>). Null/blank behaves like the param-less overload.
    /// </summary>
    Task<ClusterJoinMaterial?> GetJoinMaterialAsync(string? tailscaleAuthKeyOverride, CancellationToken ct = default);
}
