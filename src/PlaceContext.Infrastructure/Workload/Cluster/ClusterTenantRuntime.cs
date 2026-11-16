using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;
using k8s.Autorest;
using k8s.Models;
using k8s;

namespace PlaceContext.Infrastructure.Tenancy;

public sealed class ClusterTenantRuntime(
    IKubernetes client, IMemoryCache cache,
    IServiceScopeFactory? scopes = null,
    IOptions<RunBillingDeploymentOptions>? hosting = null) : ITenantRuntime
{
    internal static string Namespace(Guid tenantId) => tenantId == Guid.Empty
        ? throw new UnauthorizedAccessException("A resolved tenant is required for execution.")
        : $"pc-tenant-{tenantId:N}";

    public async Task<string> EnsureAsync(Guid tenantId, CancellationToken ct = default)
    {
        var plan = await GetDeploymentTierAsync(tenantId, ct);
        return await PrepareAsync(tenantId, plan, ct);
    }

    // Only the trusted signup worker prepares resources without an execution entitlement.
    internal async Task<string> PrepareAsync(Guid tenantId, string plan, CancellationToken ct)
    {
        var name = Namespace(tenantId);
        // Cache only the most recently reconciled tier. Upgrades/downgrades must
        // update the namespace quota instead of preserving a stale profile.
        if (cache.TryGetValue("tenant-runtime:" + name, out string? cached)
            && cached == plan)
        {
            return name;
        }
        var desired = BuildNamespace(tenantId);
        try
        {
            await client.CoreV1.CreateNamespaceAsync(desired, cancellationToken: ct);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Conflict) { }
        var existing = await client.CoreV1.ReadNamespaceAsync(name, cancellationToken: ct);
        if (desired.Metadata.Labels.Any(label => (existing.Metadata.Labels is null || !existing.Metadata.Labels.TryGetValue(label.Key, out var value) || value != label.Value))
            || existing.Metadata.DeletionTimestamp is not null)
        {
            throw new InvalidOperationException("Tenant namespace ownership or security settings do not match.");
        }
        // Reconcile every component on retry; a partially provisioned tenant never starts a workload.
        await CreateAsync(() => client.RbacAuthorizationV1.CreateNamespacedRoleBindingAsync(new V1RoleBinding
        {
            Metadata = new() { Name = "placecontext-management" },
            RoleRef = new() { ApiGroup = "rbac.authorization.k8s.io", Kind = "ClusterRole", Name = "placecontext-tenant-runtime" },
            Subjects = [new() { Kind = "ServiceAccount", Name = "placecontext", NamespaceProperty = "placecontext" }]
        }, name, cancellationToken: ct));
        await CreateAsync(() => client.CoreV1.CreateNamespacedServiceAccountAsync(new V1ServiceAccount
        {
            Metadata = new() { Name = "workload" },
            AutomountServiceAccountToken = false
        }, name, cancellationToken: ct));
        await CreateAsync(() => client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(BuildNetworkPolicy(), name, cancellationToken: ct));
        var quota = BuildResourceQuota(plan);
        try
        {
            await client.CoreV1.CreateNamespacedResourceQuotaAsync(quota, name, cancellationToken: ct);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Conflict)
        {
            var existingQuota = await client.CoreV1.ReadNamespacedResourceQuotaAsync("tenant-limits", name, cancellationToken: ct);
            if (existingQuota.Spec?.Hard is null)
            {
                throw new InvalidOperationException("Tenant quota is missing its hard limit definitions.");
            }
            if (quota.Spec.Hard.Any(limit => !existingQuota.Spec.Hard.TryGetValue(limit.Key, out var current)
                                || current.ToString() != limit.Value.ToString()))
            {
                quota.Metadata.ResourceVersion = existingQuota.Metadata.ResourceVersion;
                await client.CoreV1.ReplaceNamespacedResourceQuotaAsync(
                    quota, "tenant-limits", name, cancellationToken: ct);
            }
        }
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
        {
            throw new InvalidOperationException("Tenant runtime isolation settings do not match.");
        }
        cache.Set("tenant-runtime:" + name, plan, TimeSpan.FromMinutes(5));
        return name;
    }

    private async Task<string> GetDeploymentTierAsync(Guid tenantId, CancellationToken ct)
    {
        if (hosting?.Value.ManagedHosting != true)
        {
            return RunPlans.Developer;
        }
        if (scopes is null)
        {
            throw new InvalidOperationException("Managed tenant resource quotas require a database.");
        }
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await db.TenantRunPlans.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(row => row.TenantId == tenantId, ct);
        var now = DateTimeOffset.UtcNow;
        if (subscription is null || subscription.Plan == RunPlans.Developer
            || subscription.Status is not ("active" or "trialing")
            || subscription.PeriodStart > now || subscription.PeriodEnd <= now)
        {
            throw new InvalidOperationException("An active managed subscription is required for tenant workload provisioning.");
        }
        return subscription.Plan;
    }

    internal static V1ResourceQuota BuildResourceQuota(string plan)
    {
        var (pods, cpu, memory, storage) = plan switch
        {
            RunPlans.Basic or "starter" => ("8", "4", "8Gi", "10Gi"),
            RunPlans.Standard or "growth" => ("24", "16", "32Gi", "30Gi"),
            RunPlans.Enterprise or "business" => ("80", "64", "128Gi", "100Gi"),
            _ => ("20", "16", "32Gi", "20Gi") // Self-hosted developer retains existing defaults.
        };
        return new V1ResourceQuota
        {
            Metadata = new() { Name = "tenant-limits" },
            Spec = new()
            {
                Hard = new Dictionary<string, ResourceQuantity>
                {
                    ["pods"] = new(pods),
                    ["count/jobs.batch"] = new(pods),
                    ["count/configmaps"] = new((int.Parse(pods) * 2).ToString()),
                    ["limits.cpu"] = new(cpu),
                    ["limits.memory"] = new(memory),
                    ["persistentvolumeclaims"] = new("5"),
                    ["requests.storage"] = new(storage)
                }
            }
        };
    }

    private static async Task CreateAsync<T>(Func<Task<T>> create)
    {
        try
        {
            await create();
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Conflict) { }
    }

    internal static V1Namespace BuildNamespace(Guid tenantId) => new()
    {
        Metadata = new()
        {
            Name = Namespace(tenantId),
            Labels = new Dictionary<string, string>
            {
                ["placecontext.io/managed"] = "true",
                ["placecontext.io/kind"] = "tenant",
                ["placecontext.io/tenant"] = tenantId.ToString("D"),
                ["pod-security.kubernetes.io/enforce"] = "restricted",
                ["pod-security.kubernetes.io/audit"] = "restricted",
                ["pod-security.kubernetes.io/warn"] = "restricted"
            }
        }
    };

    internal static V1NetworkPolicy BuildNetworkPolicy() => new()
    {
        Metadata = new()
        {
            Name = "tenant-isolation"
        },
        Spec = new()
        {
            PodSelector = new(),
            PolicyTypes = ["Ingress", "Egress"],
            Ingress = [],
            Egress = []
        }
    };
}
