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
    bool IsSelf,
    /// <summary>True when the node is a Kubernetes control-plane / k3s server.</summary>
    bool IsControlPlane = false,
    /// <summary>True when this node is the designated fleet master (join codes / promote target).</summary>
    bool IsDesignatedMaster = false,
    /// <summary>Tailscale/CGNAT address when present (100.x), preferred for mesh joins.</summary>
    string? TailscaleIp = null)
{
    /// <summary>Best address for other sites to reach this node over the mesh (Tailscale first).</summary>
    public string? PreferredIp => TailscaleIp ?? InternalIp;
}
