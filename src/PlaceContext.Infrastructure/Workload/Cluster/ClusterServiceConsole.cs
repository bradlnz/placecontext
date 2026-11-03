using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Access;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Workload;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Auth;
using PlaceContext.Infrastructure.Persistence;
using k8s.Autorest;
using k8s.Models;
using k8s;

namespace PlaceContext.Infrastructure.Workload;

public sealed partial class ClusterServiceConsole(IKubernetes client, IOptions<WorkloadRunnerOptions> options,
    ICurrentTenant tenant, IPermissionService permissions, IProjectRepository projects, IChainQueueService? queues = null, AppDbContext? db = null) : IServiceConsole, IDatabaseConsole
{
    internal const string Managed = "placecontext.io/managed";
    internal const string Tenant = "placecontext.io/tenant";
    internal const string Project = "placecontext.io/project";
    internal const string Resource = "placecontext.io/resource";
    internal const string Kind = "placecontext.io/kind";
    internal const string Owner = "placecontext.io/app";
    internal const string Engine = "placecontext.io/engine";
    internal const string Name = "placecontext.io/name";
    internal const string Databases = "placecontext.io/databases";
    internal const string Queues = "placecontext.io/queues";
    internal static string AppNamespace(Guid id) => $"pc-app-{id:N}";
    internal static string DatabaseNamespace(Guid id) => $"pc-db-{id:N}";

    private static string? Value(IDictionary<string, string>? values, string key) => values is not null && values.TryGetValue(key, out var value) ? value : null;

    private async Task RequireAsync(Guid projectId, CancellationToken ct)
    {
        if (!tenant.IsResolved || !await permissions.HasAsync(Permission.SettingsManage, ct))
        {
            throw new UnauthorizedAccessException("Infrastructure requires settings management permission.");
        }
        if (await projects.GetByIdAsync(ProjectId.From(projectId), ct) is null)
        {
            throw new KeyNotFoundException("Project not found.");
        }
        ClusterWorkloadRunner.BuildRestrictedContainerSecurityContext(options.Value);
    }
    private async Task<V1Namespace> OwnedAsync(Guid projectId, Guid id, string kind, CancellationToken ct)
    {
        await RequireAsync(projectId, ct);
        var ns = await client.CoreV1.ReadNamespaceAsync(kind == "service" ? AppNamespace(id) : DatabaseNamespace(id), cancellationToken: ct);
        if (!IsOwned(ns, tenant.TenantId, projectId, id, kind))
        {
            throw new KeyNotFoundException("Resource not found in this project.");
        }
        return ns;
    }
    internal static bool IsOwned(V1Namespace ns, Guid tenantId, Guid projectId, Guid id, string kind) =>
        ns.Metadata.Labels is { } labels && Value(labels, Managed) == "true" && Value(labels, Tenant) == tenantId.ToString("D")
        && Value(labels, Project) == projectId.ToString("D") && Value(labels, Resource) == id.ToString("D") && Value(labels, Kind) == kind
        && ns.Metadata.Name == (kind == "service" ? AppNamespace(id) : DatabaseNamespace(id));

    private async Task<IReadOnlyList<V1Namespace>> ListNamespacesAsync(Guid projectId, string kind, CancellationToken ct)
    {
        await RequireAsync(projectId, ct);
        var list = await client.CoreV1.ListNamespaceAsync(labelSelector: $"{Managed}=true,{Tenant}={tenant.TenantId:D},{Project}={projectId:D},{Kind}={kind}", cancellationToken: ct);
        return list.Items.Where(ns => Guid.TryParse(Value(ns.Metadata.Labels, Resource), out var id) && IsOwned(ns, tenant.TenantId, projectId, id, kind)).ToArray();
    }
    public async Task<IReadOnlyList<ServiceInstance>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        var result = new List<ServiceInstance>();
        foreach (var ns in await ListNamespacesAsync(projectId, "service", ct))
        {
            var deployments = await client.AppsV1.ListNamespacedDeploymentAsync(ns.Metadata.Name, cancellationToken: ct);
            var deployment = deployments.Items.FirstOrDefault(item => item.Metadata.Name == "app");
            var id = Guid.Parse(ns.Metadata.Labels[Resource]);
            result.Add(new(id, ns.Metadata.Annotations[Name], deployment?.Spec.Template.Spec.Containers[0].Image ?? "",
                Status(deployment), deployment?.Status?.AvailableReplicas > 0 ? "ready" : "pending", null,
                new DateTimeOffset(ns.Metadata.CreationTimestamp ?? DateTime.UtcNow), BoundIds(deployment),
                deployment?.Spec.Template.Spec.Containers[0].Ports?.Count > 0, ns.Metadata.Name));
        }
        return result.OrderByDescending(item => item.CreatedAt).ToArray();
    }
    private static string Status(V1Deployment? deployment) => deployment is null ? "provisioning" : deployment.Spec.Replicas == 0 ? "stopped" : deployment.Status?.AvailableReplicas > 0 ? "running" : "starting";
    private static Guid[] BoundIds(V1Deployment? deployment) => (Value(deployment?.Metadata.Annotations, Databases) ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToArray();

    internal static IReadOnlyDictionary<string, string> Validate(ServiceLaunch service)
    {
        if (string.IsNullOrWhiteSpace(service.Name) || service.Name.Trim().Length > 80 || service.Name.Any(char.IsControl))
        {
            throw new ArgumentException("Enter a name of 1–80 characters.");
        }
        if (string.IsNullOrWhiteSpace(service.Image) || service.Image.Length > 256 || service.Image.StartsWith('-') || service.Image.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw new ArgumentException("Enter a valid container image reference.");
        }
        if (service.ContainerPort is < 1024 or > 65535)
        {
            throw new ArgumentException("Use a container port from 1024 to 65535.");
        }
        if (service.MemoryMb is < 64 or > 8192 || service.Cpus is < 0.1m or > 8)
        {
            throw new ArgumentException("Use 64–8192 MiB of memory and 0.1–8 CPU cores.");
        }
        if (Encoding.UTF8.GetByteCount(service.Environment) > 32768 || service.Environment.Contains('\0'))
        {
            throw new ArgumentException("Environment variables exceed the 32 KiB limit or contain null characters.");
        }
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in service.Environment.Replace("\r\n", "\n").Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2 || !Regex.IsMatch(parts[0], "^[A-Za-z_][A-Za-z0-9_]*$") || line.Contains('\r') || !env.TryAdd(parts[0], parts[1]))
            {
                throw new ArgumentException("Use one unique NAME=value environment variable per line.");
            }
        }
        return env;
    }
    public async Task<Guid> LaunchAsync(Guid projectId, ServiceLaunch service, CancellationToken ct = default)
    {
        await RequireAsync(projectId, ct);
        if (service.Code is { } code)
        {
            service = service with
            {
                Image = ValidateCode(code, options.Value).BaseImage
            };
        }
        var env = Validate(service).ToDictionary(pair => pair.Key, pair => pair.Value);
        if (service.DatabaseIds is { Count: > 0 })
        {
            throw new ArgumentException("Create the app first, then create a database assigned to it.");
        }
        if (service.ContainerPort is not null && !service.RequireSso)
        {
            throw new ArgumentException("Hosted web apps require platform SSO.");
        }
        var id = Guid.NewGuid();
        var ns = BuildNamespace(tenant.TenantId, projectId, id, "service", service.Name);
        var allowedQueues = service.QueueIds ?? [];
        if (allowedQueues.Count > 10 || allowedQueues.Distinct().Count() != allowedQueues.Count)
        {
            throw new ArgumentException("Choose at most ten distinct queues.");
        }
        if (allowedQueues.Count > 0)
        {
            if (db is null)
            {
                throw new InvalidOperationException("App execution identities are unavailable.");
            }
            var available = queues is null ? [] : await queues.ListAsync(projectId, ct);
            if (allowedQueues.Any(queueId => !available.Any(queue => queue.Id == queueId)))
            {
                throw new ArgumentException("Choose queues belonging to this project.");
            }
            if (env.Keys.Any(key => key.StartsWith("PLACECONTEXT_", StringComparison.Ordinal)))
            {
                throw new ArgumentException("PLACECONTEXT_ variables are supplied by the platform.");
            }
            env["PLACECONTEXT_API_TOKEN"] = $"pca_{id:N}." + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            env["PLACECONTEXT_API_URL"] = "http://placecontext.placecontext.svc.cluster.local";
            env["PLACECONTEXT_TENANT_HOST"] = tenant.Slug == "default" ? "localhost" : tenant.Slug + ".placecontext.io";
            env["PLACECONTEXT_QUEUE_IDS"] = string.Join(',', allowedQueues);
            // This identity exists only to authorize/audit queued runs; no human or administrator credential is shared.
            db.Users.Add(new UserRow
            {
                Id = id,
                Email = $"app-{id:N}@service.invalid",
                DisplayName = service.Name,
                Role = "HostedApp",
                PasswordHash = PasswordHasher.Hash(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))),
                PasswordSet = false
            });
            foreach (var permission in Permission.All)
            {
                db.UserPermissionGrants.Add(new UserPermissionGrantRow
                {
                    Id = Guid.NewGuid(),
                    UserId = id,
                    Permission = permission,
                    Allowed = permission is Permission.JobsRun or Permission.JobsView
                });
            }
            await db.SaveChangesAsync(ct);
        }
        ns.Metadata.Annotations[Queues] = string.Join(',', allowedQueues);
        await ProvisionNamespaceAsync(ns, ct);
        await client.CoreV1.CreateNamespacedSecretAsync(new V1Secret { Metadata = new() { Name = "app-environment" }, StringData = env.ToDictionary(pair => pair.Key, pair => pair.Value), Type = "Opaque" }, ns.Metadata.Name, cancellationToken: ct);
        await client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(BuildNetworkPolicy(ns.Metadata.Name, service.ContainerPort, [], null, service.Code?.AllowNetworkEgress == true), ns.Metadata.Name, cancellationToken: ct);
        var deployment = BuildDeployment("app", service.Image, service.MemoryMb, service.Cpus, options.Value.RunAsUser, options.Value.RunAsGroup, service.ContainerPort);
        deployment.Spec.Template.Spec.Containers[0].EnvFrom = [new() { SecretRef = new() { Name = "app-environment" } }];
        if (service.Code is { } source)
        {
            await client.CoreV1.CreateNamespacedConfigMapAsync(BuildSourceMap(source), ns.Metadata.Name, cancellationToken: ct);
            ApplySource(deployment, source, options.Value);
        }
        if (service.ContainerPort is { } port)
        {
            await client.CoreV1.CreateNamespacedServiceAsync(BuildService("app", port), ns.Metadata.Name, cancellationToken: ct);
        }
        await client.AppsV1.CreateNamespacedDeploymentAsync(deployment, ns.Metadata.Name, cancellationToken: ct);
        return id;
    }
    public async Task ControlAsync(Guid projectId, Guid serviceId, string action, CancellationToken ct = default)
    {
        var ns = await OwnedAsync(projectId, serviceId, "service", ct);
        if (action == "remove")
        {
            if ((await ListNamespacesAsync(projectId, "database", ct)).Any(database => Value(database.Metadata.Labels, Owner) == serviceId.ToString("D")))
            {
                throw new InvalidOperationException("This app owns databases. Removal is blocked to preserve their ownership and data.");
            }
            await client.CoreV1.DeleteNamespaceAsync(ns.Metadata.Name, cancellationToken: ct);
            return;
        }
        await ControlDeploymentAsync(ns.Metadata.Name, "app", action, ct);
    }
    private async Task ControlDeploymentAsync(string ns, string name, string action, CancellationToken ct)
    {
        if (action is not ("start" or "stop" or "restart"))
        {
            throw new ArgumentException("Choose start, stop, or restart.");
        }
        var deployment = await client.AppsV1.ReadNamespacedDeploymentAsync(name, ns, cancellationToken: ct);
        void ApplyServiceAction()
        {
            if (action == "restart")
            {
                deployment.Spec.Template.Metadata.Annotations ??= new Dictionary<string, string>();
                deployment.Spec.Template.Metadata.Annotations["placecontext.io/restarted-at"] = DateTimeOffset.UtcNow.ToString("O");
                return;
            }
            deployment.Spec.Replicas = action == "stop" ? 0 : 1;
        }
        ApplyServiceAction();
        await client.AppsV1.ReplaceNamespacedDeploymentAsync(deployment, name, ns, cancellationToken: ct);
    }
    public async Task<string> LogsAsync(Guid projectId, Guid serviceId, CancellationToken ct = default)
    {
        var ns = await OwnedAsync(projectId, serviceId, "service", ct);
        return await ReadLogsAsync(ns.Metadata.Name, ct);
    }
    private async Task<string> ReadLogsAsync(string ns, CancellationToken ct)
    {
        var pods = await client.CoreV1.ListNamespacedPodAsync(ns, labelSelector: "app=workload", cancellationToken: ct);
        var result = new StringBuilder("── stdout ──\n");
        foreach (var pod in pods.Items.OrderByDescending(pod => pod.Metadata.CreationTimestamp).Take(2))
        {
            if (pod.Status.Phase is "Pending")
            {
                continue;
            }
            using var stream = await client.CoreV1.ReadNamespacedPodLogAsync(pod.Metadata.Name, ns, container: "workload", tailLines: 200, limitBytes: 262144, timestamps: true, cancellationToken: ct);
            using var reader = new StreamReader(stream);
            result.AppendLine(await reader.ReadToEndAsync(ct));
        }
        return result.ToString();
    }
    public async Task<Uri> GetSsoEndpointAsync(Guid projectId, Guid serviceId, CancellationToken ct = default)
    {
        var ns = await OwnedAsync(projectId, serviceId, "service", ct);
        var deployment = await client.AppsV1.ReadNamespacedDeploymentAsync("app", ns.Metadata.Name, cancellationToken: ct);
        var port = deployment.Spec.Template.Spec.Containers[0].Ports?.SingleOrDefault()?.ContainerPort;
        if (port is null || deployment.Spec.Replicas == 0)
        {
            throw new InvalidOperationException("The web app is not running.");
        }
        return new UriBuilder("http", await ServiceAddressAsync("app", ns.Metadata.Name, ct), port.Value).Uri;
    }
    private async Task<string> ServiceAddressAsync(string name, string ns, CancellationToken ct)
    {
        // The local portal can reach service IPs without using Kubernetes-only DNS.
        var service = await client.CoreV1.ReadNamespacedServiceAsync(name, ns, cancellationToken: ct);
        if (!IPAddress.TryParse(service.Spec.ClusterIP, out var address))
        {
            throw new InvalidOperationException("The service does not have a private cluster address yet.");
        }
        return address.ToString();
    }
}
