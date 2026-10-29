using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using k8s;
using Npgsql;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Networking;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Workload;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class WorkerNetworkingTests
{
    private sealed class FakePermissionService(bool allowed) : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(allowed);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid id, string role, CancellationToken ct = default) => GetEffectivePermissionsAsync(ct);
    }
    private static WorkerNetworkPolicy Policy(params NetworkRule[] rules) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "10.20.0.0/24", new(Guid.NewGuid(), "Workers", rules));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Vpc_provisioning_is_idempotent_and_rejects_resources_owned_by_another_workspace(bool wrongOwner)
    {
        using var handler = new NetworkApi(wrongOwner);
        using var client = new Kubernetes(new KubernetesClientConfiguration { Host = "http://localhost" }, handler);
        var network = Policy();
        if (wrongOwner)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => KubeOvnNetwork.EnsureAsync(client, network, "placecontext-jobs", default));
            Assert.Single(handler.Resources);
        }
        else
        {
            await KubeOvnNetwork.EnsureAsync(client, network, "placecontext-jobs", default);
            await KubeOvnNetwork.EnsureAsync(client, network, "placecontext-jobs", default);
            Assert.Equal(2, handler.Resources.Count);
            Assert.Equal(network.Cidr, handler.Resources["subnets"]["spec"]!["cidrBlock"]!.GetValue<string>());
            Assert.Equal(network.ResourceName, handler.Resources["subnets"]["spec"]!["vpc"]!.GetValue<string>());
            Assert.Equal(network.ResourceName, KubeOvnNetwork.Annotations(network)["ovn.kubernetes.io/logical_switch"]);
        }
    }

    private sealed class NetworkApi(bool wrongOwner) : DelegatingHandler
    {
        public Dictionary<string, JsonNode> Resources { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var parts = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            var plural = parts[3];
            if (request.Method == HttpMethod.Post)
            {
                if (Resources.ContainsKey(plural)) return new(HttpStatusCode.Conflict) { RequestMessage = request, Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                var resource = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
                if (wrongOwner) resource["metadata"]!["labels"]!["placecontext.io/tenant"] = Guid.NewGuid().ToString("N");
                resource["status"] = JsonNode.Parse("""{"conditions":[{"type":"Ready","status":"True"}]}""");
                Resources.Add(plural, resource);
            }
            return new(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(Resources[plural].ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public void Policy_denies_both_directions_by_default_and_keeps_CIDR_port_rules_separate()
    {
        var deny = KubeOvnNetwork.Policy("run-1", Policy());
        Assert.Equal(["Ingress", "Egress"], deny.Spec.PolicyTypes);
        Assert.Empty(deny.Spec.Ingress);
        Assert.Empty(deny.Spec.Egress);
        var policy = KubeOvnNetwork.Policy("run-2", Policy(new("ingress", "TCP", "10.20.0.0/24", 8000, 8100), new("egress", "UDP", "10.20.0.2/32", 53, 53)));
        var inbound = Assert.Single(policy.Spec.Ingress);
        Assert.Equal("10.20.0.0/24", Assert.Single(inbound.FromProperty).IpBlock.Cidr);
        Assert.Equal(8100, Assert.Single(inbound.Ports).EndPort);
        var outbound = Assert.Single(policy.Spec.Egress);
        Assert.Equal("10.20.0.2/32", Assert.Single(outbound.To).IpBlock.Cidr);
        Assert.Equal("UDP", Assert.Single(outbound.Ports).Protocol);
        Assert.Null(outbound.Ports[0].EndPort);
        Assert.Equal("run-2", policy.Spec.PodSelector.MatchLabels["placecontext-run"]);
    }

    [Fact]
    public async Task Unsupported_runners_reject_assignments_before_launching_work()
    {
        var request = new WorkloadRunRequest("image", "{}", new Dictionary<string, string>(), [], "test", null, null, null, Network: Policy());
        var options = Options.Create(new WorkloadRunnerOptions());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DockerWorkloadRunner(options).RunAsync(request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new KubernetesWorkloadRunner(options).RunAsync(request));
        options.Value.PersistentRunnerImages = ["image"];
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PersistentWorkloadRunner(options, null!, new FakeWorkloadRunner()).RunAsync(request));
    }

    [ChainInputResumeTests.PostgresFact]
    public async Task Store_preserves_rules_and_rejects_cross_workspace_assignments_and_stale_edits()
    {
        var schema = "network_test_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES")) { SearchPath = schema }.ConnectionString;
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand($"""
            CREATE SCHEMA "{schema}";
            CREATE TABLE projects ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL);
            CREATE TABLE jobs ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL);
            """, connection)) await setup.ExecuteNonQueryAsync();
        try
        {
            var tenant = new FakeCurrentTenant(Guid.NewGuid());
            var project = Guid.NewGuid();
            var job = Guid.NewGuid();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
            await using var db = new AppDbContext(options, tenant);
            PlaceContext.Infrastructure.DependencyInjection.EnsureWorkerNetworkingSchema(db);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO projects VALUES ({project}, {tenant.TenantId})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO jobs VALUES ({job}, {tenant.TenantId}, {project})");
            var service = new DbWorkerNetworkService(db, tenant, new FakePermissionService(true));
            var group = new NetworkSecurityGroup(Guid.NewGuid(), "Web", [new("egress", "TCP", "10.20.0.0/24", 443, 443)]);
            var network = new WorkerNetwork(Guid.NewGuid(), project, "Workers", "10.20.0.0/24", [group]);
            await service.SaveAsync(network);
            await service.AssignAsync(project, job, network.Id, group.Id);
            var loaded = Assert.Single((await service.LoadAsync(project)).Networks);
            Assert.Equal(1, loaded.Version);
            Assert.Equal(group.Rules, loaded.Groups[0].Rules);
            Assert.Equal(network.Id, (await service.ResolveAsync(project, job))!.NetworkId);
            await Assert.ThrowsAsync<ArgumentException>(() => service.AssignAsync(Guid.NewGuid(), job, network.Id, group.Id));
            await Assert.ThrowsAsync<ArgumentException>(() => service.AssignAsync(project, job, network.Id, Guid.NewGuid()));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(loaded with { Name = "Renamed" }));
            await service.SaveAsync(loaded with { Groups = [group with { Name = "Renamed group" }] });
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(loaded));
            var fresh = Assert.Single((await service.LoadAsync(project)).Networks);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(fresh with { Cidr = "10.30.0.0/24" }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(fresh with { Groups = [group with { Id = Guid.NewGuid() }] }));
            var other = new FakeCurrentTenant(Guid.NewGuid());
            await using var otherDb = new AppDbContext(options, other);
            var otherService = new DbWorkerNetworkService(otherDb, other, new FakePermissionService(true));
            Assert.Empty((await otherService.LoadAsync(project)).Networks);
            Assert.Null(await otherService.ResolveAsync(project, job));
            await Assert.ThrowsAsync<ArgumentException>(() => otherService.AssignAsync(project, job, network.Id, group.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new DbWorkerNetworkService(db, tenant, new FakePermissionService(false)).SaveAsync(network));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new DbWorkerNetworkService(db, tenant, new FakePermissionService(false)).DeleteAsync(project, network.Id, fresh.Version));
            await Assert.ThrowsAsync<InvalidOperationException>(() => otherService.DeleteAsync(project, network.Id, fresh.Version));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(project, network.Id, fresh.Version));
            await service.AssignAsync(project, job, null, null);
            Assert.Null(await service.ResolveAsync(project, job));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(project, network.Id, loaded.Version));
            await service.DeleteAsync(project, network.Id, fresh.Version);
            Assert.Empty((await service.LoadAsync(project)).Networks);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
