using System.Net;
using k8s;
using k8s.Models;
using k8s.Autorest;
using Microsoft.Extensions.Caching.Memory;

namespace PlaceContext.Infrastructure.Tenancy;

public sealed class KubernetesTenantRuntime(IKubernetes client, IMemoryCache cache)
{
    internal static string Namespace(Guid tenantId) => tenantId == Guid.Empty
        ? throw new UnauthorizedAccessException("A resolved tenant is required for execution.")
        : $"pc-tenant-{tenantId:N}";

    public async Task<string> EnsureAsync(Guid tenantId, CancellationToken ct = default)
    {
        var name = Namespace(tenantId);
        if (cache.TryGetValue("tenant-runtime:" + name, out _)) return name;
        var desired = BuildNamespace(tenantId);
        try { await client.CoreV1.CreateNamespaceAsync(desired, cancellationToken: ct); }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Conflict) { }
        var existing = await client.CoreV1.ReadNamespaceAsync(name, cancellationToken: ct);
        if (desired.Metadata.Labels.Any(label => (existing.Metadata.Labels is null || !existing.Metadata.Labels.TryGetValue(label.Key, out var value) || value != label.Value))
            || existing.Metadata.DeletionTimestamp is not null)
            throw new InvalidOperationException("Tenant namespace ownership or security settings do not match.");

        // Reconcile every component on retry; a partially provisioned tenant never starts a workload.
        await CreateAsync(() => client.RbacAuthorizationV1.CreateNamespacedRoleBindingAsync(new V1RoleBinding
        {
            Metadata = new() { Name = "placecontext-management" },
            RoleRef = new() { ApiGroup = "rbac.authorization.k8s.io", Kind = "ClusterRole", Name = "placecontext-tenant-runtime" },
            Subjects = [new() { Kind = "ServiceAccount", Name = "placecontext", NamespaceProperty = "placecontext" }]
        }, name, cancellationToken: ct));
        await CreateAsync(() => client.CoreV1.CreateNamespacedServiceAccountAsync(new V1ServiceAccount
            { Metadata = new() { Name = "workload" }, AutomountServiceAccountToken = false }, name, cancellationToken: ct));
        await CreateAsync(() => client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(BuildNetworkPolicy(), name, cancellationToken: ct));
        await CreateAsync(() => client.CoreV1.CreateNamespacedResourceQuotaAsync(new V1ResourceQuota
        {
            Metadata = new() { Name = "tenant-limits" },
            Spec = new() { Hard = new Dictionary<string, ResourceQuantity>
            {
                ["pods"] = new("20"), ["count/jobs.batch"] = new("20"), ["count/configmaps"] = new("40"),
                ["limits.cpu"] = new("16"), ["limits.memory"] = new("32Gi"), ["persistentvolumeclaims"] = new("5"),
                ["requests.storage"] = new("20Gi")
            } }
        }, name, cancellationToken: ct));
        var policy = await client.NetworkingV1.ReadNamespacedNetworkPolicyAsync("tenant-isolation", name, cancellationToken: ct);
        var account = await client.CoreV1.ReadNamespacedServiceAccountAsync("workload", name, cancellationToken: ct);
        var binding = await client.RbacAuthorizationV1.ReadNamespacedRoleBindingAsync("placecontext-management", name, cancellationToken: ct);
        if (policy.Spec.PodSelector.MatchLabels?.Count > 0 || policy.Spec.PodSelector.MatchExpressions?.Count > 0
            || policy.Spec.Ingress?.Count > 0 || policy.Spec.Egress?.Count > 0
            || !policy.Spec.PolicyTypes.Contains("Ingress") || !policy.Spec.PolicyTypes.Contains("Egress")
            || account.AutomountServiceAccountToken != false
            || binding.RoleRef.Name != "placecontext-tenant-runtime" || binding.RoleRef.Kind != "ClusterRole"
            || binding.Subjects.Count != 1 || binding.Subjects[0].Name != "placecontext"
            || binding.Subjects[0].NamespaceProperty != "placecontext" || binding.Subjects[0].Kind != "ServiceAccount")
            throw new InvalidOperationException("Tenant runtime isolation settings do not match.");
        cache.Set("tenant-runtime:" + name, true, TimeSpan.FromMinutes(5));
        return name;
    }

    private static async Task CreateAsync<T>(Func<Task<T>> create)
    {
        try { await create(); }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Conflict) { }
    }

    internal static V1Namespace BuildNamespace(Guid tenantId) => new()
    {
        Metadata = new() { Name = Namespace(tenantId), Labels = new Dictionary<string, string>
        {
            ["placecontext.io/managed"] = "true", ["placecontext.io/kind"] = "tenant",
            ["placecontext.io/tenant"] = tenantId.ToString("D"),
            ["pod-security.kubernetes.io/enforce"] = "restricted",
            ["pod-security.kubernetes.io/audit"] = "restricted", ["pod-security.kubernetes.io/warn"] = "restricted"
        } }
    };

    internal static V1NetworkPolicy BuildNetworkPolicy() => new()
    {
        Metadata = new() { Name = "tenant-isolation" },
        Spec = new() { PodSelector = new(), PolicyTypes = ["Ingress", "Egress"], Ingress = [], Egress = [] }
    };
}
