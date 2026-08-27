namespace PlaceContext.Application.Ports;

public enum ClusterNodeType
{
    ControlPlane,
    StandardWorker,
    AiShard
}

/// <summary>One node in the cluster (or the single local host in dev) — enough to render a fleet view.</summary>
public sealed record ClusterNode(
    string Name,
    IReadOnlyList<string> Roles,
    bool Ready,
    string KubeletVersion,
    string OperatingSystem,
    string Architecture,
    string? InternalIp,
    string? CpuCapacity,
    string? MemoryCapacity,
    DateTimeOffset? CreatedAt,
    bool IsSelf,
    /// <summary>True when the node is a Kubernetes control-plane / k3s server.</summary>
    bool IsControlPlane = false,
    /// <summary>True when this node is the designated fleet master (join codes / promote target).</summary>
    bool IsDesignatedMaster = false,
    /// <summary>Tailscale/CGNAT address when present (100.x), preferred for mesh joins.</summary>
    string? TailscaleIp = null,
    ClusterNodeType NodeType = ClusterNodeType.StandardWorker)
{
    /// <summary>Best address for other sites to reach this node over the mesh (Tailscale first).</summary>
    public string? PreferredIp => TailscaleIp ?? InternalIp;
}

/// <summary>
/// The cluster this Host is running in: either a real multi-node Kubernetes cluster (nodes enumerated
/// via the API) or, in local dev with no cluster, a single synthetic node standing in for this host.
/// </summary>
public sealed record ClusterInfo(
    bool IsRealCluster,
    IReadOnlyList<ClusterNode> Nodes,
    string? DesignatedMasterName = null,
    /// <summary>https://&lt;master-tailnet-ip&gt;:6443 when known — used for join instructions.</summary>
    string? MasterApiUrl = null)
{
    public int NodeCount => Nodes.Count;
    public ClusterNode? DesignatedMaster =>
        DesignatedMasterName is null
            ? Nodes.FirstOrDefault(n => n.IsDesignatedMaster) ?? Nodes.FirstOrDefault(n => n.IsControlPlane)
            : Nodes.FirstOrDefault(n => string.Equals(n.Name, DesignatedMasterName, StringComparison.Ordinal));
}

/// <summary>Outcome of promoting a node to fleet master.</summary>
public sealed record PromoteMasterResult(
    string NodeName,
    bool Succeeded,
    string Message,
    /// <summary>
    /// When the target is only a worker today, the operator must reinstall k3s as a server on that
    /// machine (agent → server). This is the exact command / steps to run on that node over Tailscale.
    /// </summary>
    string? HostActionRequired = null);

/// <summary>Material for adding a worker (or extra server) that connects via the mesh.</summary>
public sealed record ClusterJoinMaterial(
    string JoinCode,
    string ServerUrl,
    bool IncludesTailscaleAuthKey,
    string Instructions);

/// <summary>
/// Reads the topology of the cluster (or local host) this Host process is running on — backs the
/// Cluster page's node inventory. Implementations: <c>KubernetesClusterInfoProvider</c> (in-cluster,
/// talks to the Kubernetes API) and <c>LocalClusterInfoProvider</c> (local dev, no cluster).
/// </summary>
public interface IClusterInfoProvider
{
    Task<ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default);
}

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
