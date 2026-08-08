namespace PlaceContext.Application.Ports;

/// <summary>
/// Reads the topology of the cluster (or local host) this Host process is running on — backs the
/// Cluster page's node inventory. Implementations: <c>KubernetesClusterInfoProvider</c> (in-cluster,
/// talks to the Kubernetes API) and <c>LocalClusterInfoProvider</c> (local dev, no cluster).
/// </summary>
public interface IClusterInfoProvider
{
    Task<ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default);
}
