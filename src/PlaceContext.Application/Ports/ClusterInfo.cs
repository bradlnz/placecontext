namespace PlaceContext.Application.Ports;

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
