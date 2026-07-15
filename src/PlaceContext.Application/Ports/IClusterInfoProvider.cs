namespace PlaceContext.Application.Ports;

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
    bool IsSelf);

/// <summary>
/// The cluster this Host is running in: either a real multi-node Kubernetes cluster (nodes enumerated
/// via the API) or, in local dev with no cluster, a single synthetic node standing in for this host.
/// </summary>
public sealed record ClusterInfo(bool IsRealCluster, IReadOnlyList<ClusterNode> Nodes)
{
    public int NodeCount => Nodes.Count;
}

/// <summary>
/// Reads the topology of the cluster (or local host) this Host process is running on — backs the
/// Cluster page's node inventory. Implementations: <c>KubernetesClusterInfoProvider</c> (in-cluster,
/// talks to the Kubernetes API) and <c>LocalClusterInfoProvider</c> (local dev, no cluster).
/// </summary>
public interface IClusterInfoProvider
{
    Task<ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default);
}
