using k8s;
using k8s.Models;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Cluster;

/// <summary>
/// In-cluster node inventory via the Kubernetes API — the Cluster page's fleet view when PlaceContext
/// runs inside k3s/k8s. Requires the Host's ServiceAccount to have <c>nodes</c> get/list RBAC; that's
/// a cluster-scoped resource, so it's granted via a ClusterRole (see deploy/k3s/placecontext.yaml),
/// not the namespaced Role the jobs runner uses.
/// </summary>
public sealed class KubernetesClusterInfoProvider : IClusterInfoProvider
{
    public async Task<ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default)
    {
        var cfg = KubernetesClientConfiguration.InClusterConfig();
        var ns = string.IsNullOrWhiteSpace(cfg.Namespace) ? "placecontext" : cfg.Namespace;
        using var client = new Kubernetes(cfg);

        var list = await client.CoreV1.ListNodeAsync(cancellationToken: ct);

        // Best-effort "which node is this pod on": HOSTNAME is the pod name by default, so read the
        // pod's own record and take Spec.NodeName. No RBAC to read this pod ⇒ no self marker, not a
        // failure — the fleet view still renders.
        string? selfNodeName = null;
        var podName = Environment.GetEnvironmentVariable("HOSTNAME");
        if (!string.IsNullOrWhiteSpace(podName))
        {
            try
            {
                var pod = await client.CoreV1.ReadNamespacedPodAsync(podName, ns, cancellationToken: ct);
                selfNodeName = pod.Spec?.NodeName;
            }
            catch { /* self-marking is a nicety, not a requirement */ }
        }

        var nodes = list.Items.Select(n => Map(n, selfNodeName)).ToList();
        return new ClusterInfo(IsRealCluster: true, Nodes: nodes);
    }

    private static ClusterNode Map(V1Node node, string? selfNodeName)
    {
        var name = node.Metadata?.Name ?? "unknown";
        var labels = node.Metadata?.Labels;
        var roles = labels?.Keys
            .Where(k => k.StartsWith("node-role.kubernetes.io/", StringComparison.Ordinal))
            .Select(k => k["node-role.kubernetes.io/".Length..])
            .Where(r => r.Length > 0)
            .ToList() ?? new List<string>();
        if (roles.Count == 0) roles.Add("worker");

        var ready = node.Status?.Conditions?.Any(c => c.Type == "Ready" && c.Status == "True") ?? false;
        var info = node.Status?.NodeInfo;
        var internalIp = node.Status?.Addresses?.FirstOrDefault(a => a.Type == "InternalIP")?.Address;

        string? cpu = null, mem = null;
        if (node.Status?.Capacity is { } cap)
        {
            if (cap.TryGetValue("cpu", out var cpuQ)) cpu = FormatCpu(cpuQ);
            if (cap.TryGetValue("memory", out var memQ)) mem = FormatBytes(memQ.ToDecimal());
        }

        return new ClusterNode(
            Name: name,
            Roles: roles,
            Ready: ready,
            KubeletVersion: info?.KubeletVersion ?? "unknown",
            OperatingSystem: string.Join('/', new[] { info?.OperatingSystem, info?.Architecture }.Where(s => !string.IsNullOrEmpty(s))),
            Architecture: info?.Architecture ?? "unknown",
            InternalIp: internalIp,
            CpuCapacity: cpu,
            MemoryCapacity: mem,
            CreatedAt: node.Metadata?.CreationTimestamp,
            IsSelf: selfNodeName is not null && string.Equals(name, selfNodeName, StringComparison.Ordinal));
    }

    private static string FormatCpu(ResourceQuantity q)
    {
        var cores = q.ToDecimal();
        return cores == Math.Floor(cores) ? $"{cores:0} vCPU" : $"{cores:0.##} vCPU";
    }

    private static string FormatBytes(decimal bytes)
    {
        if (bytes >= 1024m * 1024 * 1024) return $"{bytes / (1024m * 1024 * 1024):0.#} GiB";
        if (bytes >= 1024m * 1024) return $"{bytes / (1024m * 1024):0.#} MiB";
        if (bytes >= 1024m) return $"{bytes / 1024m:0.#} KiB";
        return $"{bytes:0} B";
    }
}
