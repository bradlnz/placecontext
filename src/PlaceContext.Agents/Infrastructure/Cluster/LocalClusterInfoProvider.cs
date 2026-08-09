using System.Runtime.InteropServices;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Infrastructure.Cluster;

/// <summary>
/// Local-dev fallback when PlaceContext is not running in a Kubernetes cluster (no
/// <c>KUBERNETES_SERVICE_HOST</c>) — reports a single synthetic node representing this machine, so
/// the Cluster page still renders something useful instead of an empty/broken state.
/// </summary>
public sealed class LocalClusterInfoProvider : IClusterInfoProvider, IClusterAdminPort
{
    public Task<ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default)
    {
        var name = Environment.MachineName;
        var node = new ClusterNode(
            Name: name,
            Roles: new[] { "local / Docker runner", "control-plane" },
            Ready: true,
            KubeletVersion: "n/a",
            OperatingSystem: $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})",
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            InternalIp: "127.0.0.1",
            CpuCapacity: $"{Environment.ProcessorCount} vCPU",
            MemoryCapacity: null,
            CreatedAt: null,
            IsSelf: true,
            IsControlPlane: true,
            IsDesignatedMaster: true,
            TailscaleIp: null);

        return Task.FromResult(new ClusterInfo(
            IsRealCluster: false,
            Nodes: new[] { node },
            DesignatedMasterName: name,
            MasterApiUrl: null));
    }

    public Task<PromoteMasterResult> PromoteToMasterAsync(string nodeName, CancellationToken ct = default)
        => Task.FromResult(new PromoteMasterResult(
            nodeName,
            Succeeded: true,
            Message: "Local single-node mode — this host is already the only master. Install a multi-node k3s fleet (with Tailscale) to promote among real nodes."));

    public Task<ClusterJoinMaterial?> GetJoinMaterialAsync(CancellationToken ct = default)
        => Task.FromResult<ClusterJoinMaterial?>(null);

    /// <summary>Local single-node dev has no join secret/mesh to embed a key into — the override is
    /// irrelevant here (there's nothing to join).</summary>
    public Task<ClusterJoinMaterial?> GetJoinMaterialAsync(string? tailscaleAuthKeyOverride, CancellationToken ct = default)
        => Task.FromResult<ClusterJoinMaterial?>(null);
}
