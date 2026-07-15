using System.Runtime.InteropServices;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Cluster;

/// <summary>
/// Local-dev fallback when PlaceContext is not running in a Kubernetes cluster (no
/// <c>KUBERNETES_SERVICE_HOST</c>) — reports a single synthetic node representing this machine, so
/// the Cluster page still renders something useful instead of an empty/broken state.
/// </summary>
public sealed class LocalClusterInfoProvider : IClusterInfoProvider
{
    public Task<ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default)
    {
        var node = new ClusterNode(
            Name: Environment.MachineName,
            Roles: new[] { "local / Docker runner" },
            Ready: true,
            KubeletVersion: "n/a",
            OperatingSystem: $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})",
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            InternalIp: "127.0.0.1",
            CpuCapacity: $"{Environment.ProcessorCount} vCPU",
            MemoryCapacity: null, // not worth a P/Invoke for a single dev-box row
            CreatedAt: null,
            IsSelf: true);

        return Task.FromResult(new ClusterInfo(IsRealCluster: false, Nodes: new[] { node }));
    }
}
