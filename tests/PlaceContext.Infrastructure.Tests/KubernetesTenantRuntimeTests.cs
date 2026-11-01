using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using k8s;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PlaceContext.Infrastructure.Tenancy;
using PlaceContext.Infrastructure.Workload;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class KubernetesTenantRuntimeTests
{
    [Fact]
    public async Task Each_tenant_gets_a_restricted_namespace_and_retries_finish_partial_provisioning()
    {
        using var api = new ClusterApi { FailQuotaOnce = true };
        using var client = new Kubernetes(new KubernetesClientConfiguration { Host = "http://localhost" }, api);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var runtime = new KubernetesTenantRuntime(client, cache);
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        await Assert.ThrowsAnyAsync<Exception>(() => runtime.EnsureAsync(owner));
        var ns = await runtime.EnsureAsync(owner);
        Assert.Equal($"pc-tenant-{owner:N}", ns);
        Assert.NotEqual(ns, await runtime.EnsureAsync(other));
        var count = api.Requests;
        Assert.Equal(ns, await runtime.EnsureAsync(owner));
        Assert.Equal(count, api.Requests);
        var policy = api.Resources.Values.First(x => x["metadata"]?["name"]?.ToString() == "tenant-isolation");
        Assert.Empty(policy["spec"]!["ingress"]!.AsArray());
        Assert.Empty(policy["spec"]!["egress"]!.AsArray());
        Assert.Empty(policy["spec"]!["podSelector"]!.AsObject());
        Assert.Contains(api.Resources.Values, x => x["metadata"]?["name"]?.ToString() == "tenant-limits");
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("policy")]
    [InlineData("account")]
    [InlineData("binding")]
    public async Task Existing_resources_with_wrong_ownership_or_weakened_isolation_are_rejected(string tamper)
    {
        using var api = new ClusterApi { Tamper = tamper };
        using var client = new Kubernetes(new KubernetesClientConfiguration { Host = "http://localhost" }, api);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new KubernetesTenantRuntime(client, cache).EnsureAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("free", "20", "16", "32Gi")]
    [InlineData("basic", "8", "4", "8Gi")]
    [InlineData("standard", "24", "16", "32Gi")]
    [InlineData("enterprise", "80", "64", "128Gi")]
    public void Managed_tiers_receive_bounded_resource_quotas(string tier, string pods, string cpu, string memory)
    {
        var quota = KubernetesTenantRuntime.BuildResourceQuota(tier);
        Assert.Equal(pods, quota.Spec.Hard["pods"].ToString());
        Assert.Equal(cpu, quota.Spec.Hard["limits.cpu"].ToString());
        Assert.Equal(memory, quota.Spec.Hard["limits.memory"].ToString());
    }

    [Fact]
    public async Task Unresolved_tenants_cannot_start_or_cancel_cluster_workloads()
    {
        var runner = new KubernetesWorkloadRunner(Options.Create(new WorkloadRunnerOptions()), tenant: new FakeCurrentTenant(Guid.Empty));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => runner.RunAsync(new("image", "{}", new Dictionary<string, string>(), [], "run", null, null, null)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => runner.CancelAsync("run"));
    }

    private sealed class ClusterApi : DelegatingHandler
    {
        public Dictionary<string, JsonObject> Resources { get; } = [];
        public bool FailQuotaOnce;
        public string? Tamper;
        public int Requests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                if (path.EndsWith("resourcequotas") && FailQuotaOnce)
                { FailQuotaOnce = false; return Reply(HttpStatusCode.Forbidden, new()); }
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
                var key = path + "/" + body["metadata"]!["name"];
                if (!Resources.TryAdd(key, body)) return Reply(HttpStatusCode.Conflict, new());
                return Reply(HttpStatusCode.Created, body);
            }
            if (request.Method == HttpMethod.Put)
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
                Resources[path] = body;
                return Reply(HttpStatusCode.OK, body);
            }
            var value = Resources[path].DeepClone().AsObject();
            if (Tamper == "owner" && path.StartsWith("/api/v1/namespaces/"))
                value["metadata"]!["labels"]!["placecontext.io/tenant"] = Guid.NewGuid().ToString();
            if (Tamper == "policy" && path.EndsWith("tenant-isolation")) value["spec"]!["ingress"]!.AsArray().Add(new JsonObject());
            if (Tamper == "account" && path.EndsWith("serviceaccounts/workload")) value["automountServiceAccountToken"] = true;
            if (Tamper == "binding" && path.EndsWith("placecontext-management")) value["roleRef"]!["name"] = "cluster-admin";
            return Reply(HttpStatusCode.OK, value);
            HttpResponseMessage Reply(HttpStatusCode status, JsonObject body) => new(status)
            { RequestMessage = request, Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
}
