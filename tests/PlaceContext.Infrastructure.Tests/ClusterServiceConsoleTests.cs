using System.Net;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Workload;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Workload;
using PlaceContext.TestSupport;
using k8s.Models;
using k8s;
using PlaceContext.Application.Access;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ClusterServiceConsoleTests
{
    [Theory]
    [InlineData("10.43.152.205", "http://10.43.152.205:8080/")]
    [InlineData("fd00::1", "http://[fd00::1]:8080/")]
    [InlineData("None", null)]
    [InlineData(null, null)]
    public async Task App_endpoint_uses_the_private_service_IP_without_cluster_DNS(string? address, string? expected)
    {
        var tenant = Guid.NewGuid();
        var id = Guid.NewGuid();
        var project = Project.Discover(ProjectPath.From("/demo"), ProjectName.From("Demo"), DateTimeOffset.UtcNow);
        var projects = new InMemoryProjectRepository();
        await projects.AddAsync(project);
        var ns = ClusterServiceConsole.BuildNamespace(tenant, project.Id.Value, id, "service", "Demo");
        using var handler = new ServiceApi(ns, address);
        using var client = new Kubernetes(new KubernetesClientConfiguration { Host = "http://localhost" }, handler);
        var console = new ClusterServiceConsole(client, Options.Create(new WorkloadRunnerOptions()), new FakeCurrentTenant(tenant), new Permissions(true), projects);
        async Task AssertPersistenceAsync()
        {
            if (expected is null)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => console.GetSsoEndpointAsync(project.Id.Value, id));
                return;
            }
            Assert.Equal(expected, (await console.GetSsoEndpointAsync(project.Id.Value, id)).AbsoluteUri);
        }
        await AssertPersistenceAsync();
        Assert.Equal($"/api/v1/namespaces/{ns.Metadata.Name}/services/app", handler.LastPath);
    }

    private sealed class ServiceApi(V1Namespace ns, string? address) : DelegatingHandler
    {
        public string? LastPath
        {
            get; private set;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastPath = request.RequestUri!.AbsolutePath;
            var body = LastPath switch
            {
                var path when path == $"/api/v1/namespaces/{ns.Metadata.Name}" => JsonSerializer.Serialize(new { metadata = new { name = ns.Metadata.Name, labels = ns.Metadata.Labels } }),
                var path when path == $"/apis/apps/v1/namespaces/{ns.Metadata.Name}/deployments/app" => """{"spec":{"replicas":1,"template":{"spec":{"containers":[{"name":"workload","ports":[{"containerPort":8080}]}]}}}}""",
                var path when path == $"/api/v1/namespaces/{ns.Metadata.Name}/services/app" => JsonSerializer.Serialize(new { spec = new { clusterIP = address } }),
                _ => throw new InvalidOperationException("Unexpected Kubernetes request: " + LastPath),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [Theory]
    [InlineData("", "app:1", 8080, "")]
    [InlineData("portal", "--privileged", 8080, "")]
    [InlineData("portal", "app:1 extra", 8080, "")]
    [InlineData("portal", "app:1", 80, "")]
    [InlineData("portal", "app:1", 65536, "")]
    [InlineData("portal", "app:1", 8080, "TOKEN")]
    [InlineData("portal", "app:1", 8080, "TOKEN=a\nTOKEN=b")]
    public void Invalid_input_is_rejected(string name, string image, int port, string environment) =>
        Assert.Throws<ArgumentException>(() => ClusterServiceConsole.Validate(new(name, image, port, Environment: environment)));

    [Fact]
    public void Each_resource_gets_a_distinct_restricted_namespace_and_ownership_is_checked()
    {
        var tenant = Guid.NewGuid();
        var project = Guid.NewGuid();
        var id = Guid.NewGuid();
        var app = ClusterServiceConsole.BuildNamespace(tenant, project, id, "service", "Portal");
        var database = ClusterServiceConsole.BuildNamespace(tenant, project, id, "database", "Data", id, "postgres");
        Assert.NotEqual(app.Metadata.Name, database.Metadata.Name);
        Assert.Equal("restricted", app.Metadata.Labels["pod-security.kubernetes.io/enforce"]);
        Assert.True(ClusterServiceConsole.IsOwned(app, tenant, project, id, "service"));
        Assert.False(ClusterServiceConsole.IsOwned(app, Guid.NewGuid(), project, id, "service"));
        Assert.False(ClusterServiceConsole.IsOwned(app, tenant, Guid.NewGuid(), id, "service"));
        Assert.False(ClusterServiceConsole.IsOwned(database, tenant, project, id, "service"));
        ClusterServiceConsole.RequireDatabaseOwner(database, id);
        Assert.Throws<UnauthorizedAccessException>(() => ClusterServiceConsole.RequireDatabaseOwner(database, Guid.NewGuid()));
        database.Metadata.Labels.Remove(ClusterServiceConsole.Owner);
        Assert.Throws<UnauthorizedAccessException>(() => ClusterServiceConsole.RequireDatabaseOwner(database, id));
    }

    [Theory]
    [InlineData("postgres", 70, 70)]
    [InlineData("redis", 999, 1000)]
    public void Databases_are_non_root_token_free_and_keep_data_on_a_pvc(string engine, long uid, long gid)
    {
        var deployment = ClusterServiceConsole.BuildDatabaseDeployment(new("Data", engine, ServiceId: Guid.NewGuid()));
        var pod = deployment.Spec.Template.Spec;
        var container = Assert.Single(pod.Containers);
        Assert.Equal("workload", pod.ServiceAccountName);
        Assert.False(pod.AutomountServiceAccountToken);
        Assert.True(pod.SecurityContext.RunAsNonRoot);
        Assert.Equal(uid, container.SecurityContext.RunAsUser);
        Assert.Equal(gid, container.SecurityContext.RunAsGroup);
        Assert.True(container.SecurityContext.ReadOnlyRootFilesystem);
        Assert.False(container.SecurityContext.Privileged);
        Assert.False(container.SecurityContext.AllowPrivilegeEscalation);
        Assert.Equal("ALL", Assert.Single(container.SecurityContext.Capabilities.Drop));
        Assert.Equal("RuntimeDefault", pod.SecurityContext.SeccompProfile.Type);
        Assert.Contains(pod.Volumes, volume => volume.PersistentVolumeClaim?.ClaimName == "data");
        Assert.DoesNotContain(pod.Volumes, volume => volume.HostPath is not null);
        Assert.Null(pod.InitContainers);
        Assert.Equal("ClusterIP", ClusterServiceConsole.BuildService("database", 5432).Spec.Type);
    }

    [Theory]
    [InlineData(0, 65534)]
    [InlineData(65534, 0)]
    [InlineData(-1, 65534)]
    public void Service_root_overrides_are_rejected(long uid, long gid) => Assert.Throws<ArgumentException>(() =>
        ClusterServiceConsole.BuildDeployment("app", "image", 256, 1, uid, gid, 8080));

    [Fact]
    public void Database_traffic_is_allowed_only_from_its_owner_and_the_portal()
    {
        var owner = ClusterServiceConsole.AppNamespace(Guid.NewGuid());
        var policy = ClusterServiceConsole.BuildNetworkPolicy("pc-db-test", null, [], (owner, 5432));
        Assert.Equal(new[] { "Ingress", "Egress" }, policy.Spec.PolicyTypes);
        var ingress = Assert.Single(policy.Spec.Ingress);
        Assert.Equal(2, ingress.FromProperty.Count);
        Assert.Contains(ingress.FromProperty, peer => peer.NamespaceSelector.MatchLabels["kubernetes.io/metadata.name"] == owner && peer.PodSelector.MatchLabels["app"] == "workload");
        Assert.All(policy.Spec.Egress.SelectMany(rule => rule.To), peer => Assert.Null(peer.IpBlock));
        Assert.Equal("kube-system", Assert.Single(Assert.Single(policy.Spec.Egress).To).NamespaceSelector.MatchLabels["kubernetes.io/metadata.name"]);
    }

    [Fact]
    public void An_unassigned_database_and_mutating_redis_commands_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ClusterServiceConsole.ValidateDatabase(new("Data", "postgres")));
        Assert.Throws<ArgumentException>(() => ClusterServiceConsole.RedisReadCommand("FLUSHALL"));
        Assert.Throws<ArgumentException>(() => ClusterServiceConsole.RedisReadCommand("SET secret value"));
        Assert.Equal(("GET", "a key with spaces"), ClusterServiceConsole.RedisReadCommand("GET a key with spaces"));
    }

    [Fact]
    public async Task Permission_denial_happens_before_any_cluster_call()
    {
        using var client = new Kubernetes(new KubernetesClientConfiguration { Host = "http://127.0.0.1:1" });
        var console = new ClusterServiceConsole(client, Options.Create(new WorkloadRunnerOptions()), new FakeCurrentTenant(Guid.NewGuid()), new Permissions(false), new InMemoryProjectRepository());
        var project = Guid.NewGuid();
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => console.ListAsync(project));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => console.LaunchAsync(project, new("App", "image", 8080)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => console.ControlAsync(project, id, "remove"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => console.GetSsoEndpointAsync(project, id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => console.QueryAsync(project, id, "SELECT 1"));
    }
    [Fact]
    public void Source_apps_preserve_sandbox_database_bindings_and_run_dependency_setup()
    {
        var options = new WorkloadRunnerOptions();
        var deployment = ClusterServiceConsole.BuildDeployment("app", "old", 512, 1, 65534, 65534, 8080);
        deployment.Spec.Template.Spec.Containers[0].EnvFrom = [new() { SecretRef = new() { Name = "database-connections" } }];
        deployment.Metadata.Annotations[ClusterServiceConsole.Databases] = "owned-database";
        var code = new ServiceCode("node", "src/index.js", [new("src/index.js", "console.log('app')"), new("package.json", "{}")], true);
        ClusterServiceConsole.ApplySource(deployment, code, options);
        ClusterServiceConsole.ApplySource(deployment, code, options);
        var container = Assert.Single(deployment.Spec.Template.Spec.Containers);
        Assert.Equal("node:22-slim", container.Image);
        Assert.Equal("database-connections", Assert.Single(container.EnvFrom).SecretRef.Name);
        Assert.Equal("owned-database", deployment.Metadata.Annotations[ClusterServiceConsole.Databases]);
        Assert.Contains("npm install", Assert.Single(container.Args));
        Assert.Contains("exec 'node' '/work/src/index.js'", Assert.Single(container.Args));
        Assert.True(container.SecurityContext.ReadOnlyRootFilesystem);
        Assert.False(deployment.Spec.Template.Spec.AutomountServiceAccountToken);
        Assert.Equal(3, deployment.Spec.Template.Spec.Volumes.Count);
        var source = Assert.Single(deployment.Spec.Template.Spec.Volumes.Where(volume => volume.Name == "source"));
        Assert.Contains(source.ConfigMap.Items, file => file.Path == "src/index.js");
        Assert.DoesNotContain(source.ConfigMap.Items, file => file.Key == "definition");
        Assert.Contains("console.log('app')", ClusterServiceConsole.BuildSourceMap(code).Data.Values);
    }

    [Theory]
    [InlineData("../main.py")]
    [InlineData("/main.py")]
    [InlineData("a/./main.py")]
    [InlineData("a//main.py")]
    public void Source_paths_cannot_escape_or_alias_the_work_directory(string path) =>
        Assert.Throws<ArgumentException>(() => ClusterServiceConsole.ValidateCode(new("python", path, [new(path, "pass")]), new()));

    [Fact]
    public void Source_dependencies_require_explicit_public_egress_and_keep_private_networks_blocked()
    {
        var code = new ServiceCode("python", "main.py", [new("main.py", "pass"), new("requirements.txt", "psycopg[binary]")]);
        Assert.Throws<ArgumentException>(() => ClusterServiceConsole.ValidateCode(code, new()));
        Assert.NotNull(ClusterServiceConsole.ValidateCode(code with
        {
            AllowNetworkEgress = true
        }, new()));
        var policy = ClusterServiceConsole.BuildNetworkPolicy("app", 8080, [("owned-db", 5432)], null, true);
        var internet = Assert.Single(policy.Spec.Egress.SelectMany(rule => rule.To).Where(peer => peer.IpBlock is not null));
        Assert.Contains("10.0.0.0/8", internet.IpBlock.Except);
        Assert.Contains("192.168.0.0/16", internet.IpBlock.Except);
        Assert.Contains(policy.Spec.Egress.SelectMany(rule => rule.To), peer => peer.NamespaceSelector?.MatchLabels is { } labels && labels.TryGetValue("kubernetes.io/metadata.name", out var name) && name == "owned-db");
    }

    private sealed class Permissions(bool allowed) : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(allowed);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid id, string role, CancellationToken ct = default) => GetEffectivePermissionsAsync(ct);
    }
}
