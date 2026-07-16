using System.Net;
using System.Text;
using k8s;
using k8s.Models;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Cluster;

/// <summary>
/// In-cluster node inventory + fleet-master admin via the Kubernetes API. Designated master lives in
/// ConfigMap <c>placecontext-cluster</c>; join token (for portal join codes) in Secret
/// <c>placecontext-cluster-join</c> when seeded by the installer on a k3s server.
/// </summary>
public sealed class KubernetesClusterInfoProvider : IClusterInfoProvider, IClusterAdminPort
{
    public const string ClusterConfigMap = "placecontext-cluster";
    public const string JoinSecret = "placecontext-cluster-join";
    public const string MasterAnnotation = "placecontext.ai/designated-master";
    private const string MasterKey = "designatedMaster";

    public async Task<ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default)
    {
        var (client, ns) = CreateClient();
        using (client)
        {
            var list = await client.CoreV1.ListNodeAsync(cancellationToken: ct);
            var designated = await ReadDesignatedMasterAsync(client, ns, ct);
            var selfNodeName = await TrySelfNodeNameAsync(client, ns, ct);

            var nodes = list.Items.Select(n => Map(n, selfNodeName, designated)).ToList();

            // If nothing designated yet, treat the first ready control-plane as master for display.
            if (string.IsNullOrWhiteSpace(designated))
            {
                var fallback = nodes.FirstOrDefault(n => n.IsControlPlane && n.Ready)
                               ?? nodes.FirstOrDefault(n => n.IsControlPlane)
                               ?? nodes.FirstOrDefault();
                if (fallback is not null)
                {
                    designated = fallback.Name;
                    nodes = nodes.Select(n => n with
                    {
                        IsDesignatedMaster = string.Equals(n.Name, designated, StringComparison.Ordinal)
                    }).ToList();
                }
            }

            var master = nodes.FirstOrDefault(n => n.IsDesignatedMaster);
            var apiUrl = master?.PreferredIp is { Length: > 0 } ip
                ? $"https://{ip}:6443"
                : null;

            return new ClusterInfo(
                IsRealCluster: true,
                Nodes: nodes,
                DesignatedMasterName: designated,
                MasterApiUrl: apiUrl);
        }
    }

    public async Task<PromoteMasterResult> PromoteToMasterAsync(string nodeName, CancellationToken ct = default)
    {
        var (client, ns) = CreateClient();
        using (client)
        {
            V1Node? node;
            try
            {
                node = await client.CoreV1.ReadNodeAsync(nodeName, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                return new PromoteMasterResult(nodeName, false, $"Node '{nodeName}' not found: {ex.Message}");
            }

            var isControlPlane = IsControlPlane(node);
            var tsIp = FindTailscaleIp(node);
            var internalIp = node.Status?.Addresses?.FirstOrDefault(a => a.Type == "InternalIP")?.Address;
            var preferred = tsIp ?? internalIp;

            await WriteDesignatedMasterAsync(client, ns, nodeName, preferred, ct);
            await AnnotateNodesAsync(client, nodeName, ct);

            if (isControlPlane)
            {
                var mesh = tsIp is not null
                    ? $" Mesh address: {tsIp} (Tailscale). Other sites (e.g. DigitalOcean) should join via this address."
                    : " Tip: install Tailscale on this node so join codes use a stable mesh IP.";
                return new PromoteMasterResult(
                    nodeName,
                    Succeeded: true,
                    Message: $"'{nodeName}' is now the designated fleet master.{mesh}");
            }

            // Worker only — API cannot turn an agent into a k3s server. Hand the operator the host steps.
            var join = await TryReadJoinSecretAsync(client, ns, ct);
            var existingMasterUrl = join?.ServerUrl
                ?? (await GetClusterInfoAsync(ct)).MasterApiUrl
                ?? "https://<current-master-tailscale-ip>:6443";
            var tokenHint = join?.Token is { Length: > 0 } t
                ? t
                : "$(sudo cat /var/lib/rancher/k3s/server/node-token)";

            var hostAction = $"""
                '{nodeName}' is a worker today. Designate is saved, but k3s control-plane must be installed on the host.

                On {nodeName} (SSH or console), over Tailscale to the current master:

                  # Stop the agent and reinstall as an additional server (HA control-plane)
                  sudo /usr/local/bin/k3s-agent-uninstall.sh 2>/dev/null || true
                  curl -sfL https://get.k3s.io | \
                    K3S_URL='{existingMasterUrl}' \
                    K3S_TOKEN='{tokenHint}' \
                    sh -s - server

                Ensure Tailscale is up on both the Mac master and this node so {existingMasterUrl} is reachable.
                After the node shows role control-plane, refresh Cluster — join codes will target this master.
                """;

            return new PromoteMasterResult(
                nodeName,
                Succeeded: true,
                Message: $"'{nodeName}' is designated fleet master, but still needs a host-side promote to k3s server.",
                HostActionRequired: hostAction.Trim());
        }
    }

    public async Task<ClusterJoinMaterial?> GetJoinMaterialAsync(CancellationToken ct = default)
    {
        var (client, ns) = CreateClient();
        using (client)
        {
            var info = await GetClusterInfoAsync(ct);
            var join = await TryReadJoinSecretAsync(client, ns, ct);
            if (join is null || string.IsNullOrWhiteSpace(join.Token))
                return null;

            var master = info.DesignatedMaster;
            var ip = master?.PreferredIp;
            if (string.IsNullOrWhiteSpace(ip) && !string.IsNullOrWhiteSpace(join.ServerUrl))
            {
                // fall back to URL host from secret
                if (Uri.TryCreate(join.ServerUrl, UriKind.Absolute, out var u))
                    ip = u.Host;
            }

            if (string.IsNullOrWhiteSpace(ip))
                return null;

            var serverUrl = $"https://{ip}:6443";
            var tsKey = join.TailscaleAuthKey ?? "";
            var payload = string.IsNullOrWhiteSpace(tsKey)
                ? $"{serverUrl} {join.Token}"
                : $"{serverUrl} {join.Token} {tsKey}";
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var code = $"PC2.{b64}";

            var instructions = string.IsNullOrWhiteSpace(tsKey)
                ? $"""
                  On the new machine (e.g. DigitalOcean droplet):
                    1. Install Tailscale and join the same tailnet as the master ({master?.Name ?? "master"}).
                    2. curl -fsSL https://get.placecontext.ai/install.sh | bash
                    3. placecontext connect --code {code}
                       (or: sudo placecontext  → Connect, paste the code)

                  Server API (over Tailscale): {serverUrl}
                  The joining host must reach the master on the mesh; without an embedded auth key,
                  Tailscale must already be up before connect.
                  """
                : $"""
                  On the new machine (e.g. DigitalOcean droplet):
                    curl -fsSL https://get.placecontext.ai/install.sh | bash
                    placecontext connect --code {code}

                  The code includes a Tailscale auth key and the master mesh address ({serverUrl}),
                  so a fresh host can join the tailnet and the k3s fleet in one step.
                  """;

            return new ClusterJoinMaterial(
                JoinCode: code,
                ServerUrl: serverUrl,
                IncludesTailscaleAuthKey: !string.IsNullOrWhiteSpace(tsKey),
                Instructions: instructions.Trim());
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static (Kubernetes client, string ns) CreateClient()
    {
        var cfg = KubernetesClientConfiguration.InClusterConfig();
        var ns = string.IsNullOrWhiteSpace(cfg.Namespace) ? "placecontext" : cfg.Namespace;
        return (new Kubernetes(cfg), ns);
    }

    private static async Task<string?> TrySelfNodeNameAsync(Kubernetes client, string ns, CancellationToken ct)
    {
        var podName = Environment.GetEnvironmentVariable("HOSTNAME");
        if (string.IsNullOrWhiteSpace(podName)) return null;
        try
        {
            var pod = await client.CoreV1.ReadNamespacedPodAsync(podName, ns, cancellationToken: ct);
            return pod.Spec?.NodeName;
        }
        catch { return null; }
    }

    private static ClusterNode Map(V1Node node, string? selfNodeName, string? designatedMaster)
    {
        var name = node.Metadata?.Name ?? "unknown";
        var labels = node.Metadata?.Labels;
        var roles = labels?.Keys
            .Where(k => k.StartsWith("node-role.kubernetes.io/", StringComparison.Ordinal))
            .Select(k => k["node-role.kubernetes.io/".Length..])
            .Where(r => r.Length > 0)
            .ToList() ?? new List<string>();
        var controlPlane = IsControlPlane(node);
        if (roles.Count == 0) roles.Add(controlPlane ? "control-plane" : "worker");

        var ready = node.Status?.Conditions?.Any(c => c.Type == "Ready" && c.Status == "True") ?? false;
        var info = node.Status?.NodeInfo;
        var internalIp = node.Status?.Addresses?.FirstOrDefault(a => a.Type == "InternalIP")?.Address;
        var tsIp = FindTailscaleIp(node);

        string? cpu = null, mem = null;
        if (node.Status?.Capacity is { } cap)
        {
            if (cap.TryGetValue("cpu", out var cpuQ)) cpu = FormatCpu(cpuQ);
            if (cap.TryGetValue("memory", out var memQ)) mem = FormatBytes(memQ.ToDecimal());
        }

        var isDesignated = !string.IsNullOrWhiteSpace(designatedMaster)
            && string.Equals(name, designatedMaster, StringComparison.Ordinal);

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
            IsSelf: selfNodeName is not null && string.Equals(name, selfNodeName, StringComparison.Ordinal),
            IsControlPlane: controlPlane,
            IsDesignatedMaster: isDesignated,
            TailscaleIp: tsIp);
    }

    private static bool IsControlPlane(V1Node node)
    {
        var labels = node.Metadata?.Labels;
        if (labels is null) return false;
        foreach (var k in labels.Keys)
        {
            if (k.Equals("node-role.kubernetes.io/control-plane", StringComparison.Ordinal)) return true;
            if (k.Equals("node-role.kubernetes.io/master", StringComparison.Ordinal)) return true;
            if (k.Equals("node.kubernetes.io/instance-type", StringComparison.Ordinal)) { /* ignore */ }
        }
        // k3s marks servers with control-plane; also accept master legacy
        return labels.ContainsKey("node-role.kubernetes.io/control-plane")
            || labels.ContainsKey("node-role.kubernetes.io/master");
    }

    /// <summary>Tailscale/Headscale CGNAT 100.64.0.0/10 (and common 100.x advertisements).</summary>
    private static string? FindTailscaleIp(V1Node node)
    {
        var addrs = node.Status?.Addresses;
        if (addrs is null) return null;
        foreach (var a in addrs)
        {
            if (a.Address is null) continue;
            if (IPAddress.TryParse(a.Address, out var ip) && IsTailscaleRange(ip))
                return a.Address;
        }
        // Annotation override if ops stamped it
        if (node.Metadata?.Annotations is { } ann
            && ann.TryGetValue("placecontext.ai/tailscale-ip", out var stamped)
            && !string.IsNullOrWhiteSpace(stamped))
            return stamped.Trim();
        return null;
    }

    private static bool IsTailscaleRange(IPAddress ip)
    {
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        // 100.64.0.0/10 → 100.64.0.0 – 100.127.255.255
        return b[0] == 100 && b[1] >= 64 && b[1] <= 127;
    }

    private static async Task<string?> ReadDesignatedMasterAsync(Kubernetes client, string ns, CancellationToken ct)
    {
        try
        {
            var cm = await client.CoreV1.ReadNamespacedConfigMapAsync(ClusterConfigMap, ns, cancellationToken: ct);
            if (cm.Data is not null && cm.Data.TryGetValue(MasterKey, out var v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        catch { /* not created yet */ }
        return null;
    }

    private static async Task WriteDesignatedMasterAsync(
        Kubernetes client, string ns, string nodeName, string? preferredIp, CancellationToken ct)
    {
        var data = new Dictionary<string, string> { [MasterKey] = nodeName };
        if (!string.IsNullOrWhiteSpace(preferredIp))
            data["masterEndpoint"] = $"https://{preferredIp}:6443";

        try
        {
            var existing = await client.CoreV1.ReadNamespacedConfigMapAsync(ClusterConfigMap, ns, cancellationToken: ct);
            existing.Data ??= new Dictionary<string, string>();
            foreach (var (k, v) in data) existing.Data[k] = v;
            await client.CoreV1.ReplaceNamespacedConfigMapAsync(existing, ClusterConfigMap, ns, cancellationToken: ct);
        }
        catch
        {
            var cm = new V1ConfigMap
            {
                Metadata = new V1ObjectMeta { Name = ClusterConfigMap, NamespaceProperty = ns },
                Data = data
            };
            await client.CoreV1.CreateNamespacedConfigMapAsync(cm, ns, cancellationToken: ct);
        }
    }

    private static async Task AnnotateNodesAsync(Kubernetes client, string masterName, CancellationToken ct)
    {
        V1NodeList list;
        try { list = await client.CoreV1.ListNodeAsync(cancellationToken: ct); }
        catch { return; }

        foreach (var n in list.Items)
        {
            var name = n.Metadata?.Name;
            if (name is null) continue;
            n.Metadata!.Annotations ??= new Dictionary<string, string>();
            var want = string.Equals(name, masterName, StringComparison.Ordinal) ? "true" : "false";
            if (n.Metadata.Annotations.TryGetValue(MasterAnnotation, out var cur) && cur == want)
                continue;
            n.Metadata.Annotations[MasterAnnotation] = want;
            try
            {
                await client.CoreV1.ReplaceNodeAsync(n, name, cancellationToken: ct);
            }
            catch { /* patch best-effort; ConfigMap is source of truth */ }
        }
    }

    private sealed record JoinSecretData(string Token, string? ServerUrl, string? TailscaleAuthKey);

    private static async Task<JoinSecretData?> TryReadJoinSecretAsync(Kubernetes client, string ns, CancellationToken ct)
    {
        try
        {
            var sec = await client.CoreV1.ReadNamespacedSecretAsync(JoinSecret, ns, cancellationToken: ct);
            if (sec.Data is null) return null;
            string? Dec(string key)
            {
                if (!sec.Data.TryGetValue(key, out var raw) || raw is null || raw.Length == 0) return null;
                return Encoding.UTF8.GetString(raw);
            }
            var token = Dec("node-token");
            if (string.IsNullOrWhiteSpace(token)) return null;
            return new JoinSecretData(token!, Dec("server-url"), Dec("ts-authkey"));
        }
        catch { return null; }
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
