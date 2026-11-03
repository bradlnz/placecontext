using System.Text.Json;
using System.Text;
using PlaceContext.Application.Workload;
using PlaceContext.Domain.ValueObjects;
using k8s.Models;
using k8s;

namespace PlaceContext.Infrastructure.Workload;

public sealed partial class ClusterServiceConsole
{
    internal const string Internet = "placecontext.io/public-egress";
    private const string SourceName = "app-source";

    internal static RuntimeDefinition ValidateCode(ServiceCode code, WorkloadRunnerOptions options)
    {
        if (code.Files is null || code.Files.Count is < 1 or > 100 || string.IsNullOrWhiteSpace(code.Entrypoint))
        {
            throw new ArgumentException("Provide an entrypoint and 1–100 source files.");
        }
        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(code)) > 400_000)
        {
            throw new ArgumentException("App source must fit within 400 KB. Use a prebuilt image for larger apps.");
        }
        if (code.RuntimeId is not ("python" or "node") || !options.Runtimes.TryGetValue(code.RuntimeId, out var runtime))
        {
            throw new ArgumentException("Source apps support the configured python and node runtimes.");
        }
        foreach (var file in code.Files)
        {
            if (file.Path != new CodeFile(file.Path, file.Content).Path || file.Path.Split('/').Any(part => part is "" or "." or "..") || file.Path.Any(char.IsControl))
            {
                throw new ArgumentException("Use normalized relative source paths without empty or dot segments.");
            }
        }
        _ = new WorkloadSource.CodeWorkload(code.RuntimeId, code.Files.Select(file => new CodeFile(file.Path, file.Content)).ToArray(), code.Entrypoint);
        var files = code.Files.Select(file => (file.Path, file.Content)).ToArray();
        if (WorkloadDependencies.For(code.RuntimeId, files) is not null && !code.AllowNetworkEgress)
        {
            throw new ArgumentException("Dependency manifests require AllowNetworkEgress for package downloads. Omit manifests for a dependency-free app.");
        }
        return runtime;
    }

    internal static V1ConfigMap BuildSourceMap(ServiceCode code) => new()
    {
        Metadata = new()
        {
            Name = SourceName
        },
        Data = code.Files.Select((file, index) => (Key: $"file-{index}", file.Content))
            .Append(("definition", JsonSerializer.Serialize(code))).ToDictionary(file => file.Item1, file => file.Item2),
    };

    internal static void ApplySource(V1Deployment deployment, ServiceCode code, WorkloadRunnerOptions options)
    {
        var runtime = ValidateCode(code, options);
        var pod = deployment.Spec.Template.Spec;
        var container = pod.Containers[0];
        container.Image = runtime.BaseImage;
        container.WorkingDir = "/work";
        container.VolumeMounts = container.VolumeMounts.Where(v => v.Name is not ("source" or "work")).ToList();
        container.VolumeMounts.Add(new()
        {
            Name = "source",
            MountPath = "/source",
            ReadOnlyProperty = true
        });
        container.VolumeMounts.Add(new()
        {
            Name = "work",
            MountPath = "/work"
        });
        pod.Volumes = pod.Volumes.Where(v => v.Name is not ("source" or "work")).ToList();
        pod.Volumes.Add(new()
        {
            Name = "source",
            ConfigMap = new()
            {
                Name = SourceName,
                DefaultMode = 292,
                Items = code.Files.Select((file, index) => new V1KeyToPath { Key = $"file-{index}", Path = file.Path }).ToArray()
            }
        });
        pod.Volumes.Add(new()
        {
            Name = "work",
            EmptyDir = new()
            {
                SizeLimit = new("1Gi")
            }
        });
        container.Env = [new() { Name = "HOME", Value = "/tmp" }, new() { Name = "PYTHONUNBUFFERED", Value = "1" },
            new() { Name = "PORT", Value = (container.Ports?.FirstOrDefault()?.ContainerPort ?? 8080).ToString(System.Globalization.CultureInfo.InvariantCulture) }];
        var recipe = WorkloadDependencies.For(code.RuntimeId, code.Files.Select(file => (file.Path, file.Content)).ToArray());
        var invoke = string.Join(" ", runtime.InvokeCommand.Select(arg => "'" + arg.Replace("{entrypoint}", code.Entrypoint).Replace("'", "'\\''") + "'"));
        container.Command = ["sh", "-c"];
        container.Args = ["set -eu\ncp -RL /source/. /work/\nfind /work -mindepth 1 -exec chmod u+w {} +\ncd /work\n" + (recipe is null ? "" : WorkloadDependencies.ShellPreamble(recipe)) + "\nexec " + invoke];
        deployment.Metadata.Annotations[Internet] = code.AllowNetworkEgress ? "true" : "false";
        deployment.Spec.Template.Metadata.Annotations ??= new Dictionary<string, string>();
        deployment.Spec.Template.Metadata.Annotations["placecontext.io/source-version"] = Guid.NewGuid().ToString("N");
    }

    public async Task<ServiceCode?> GetCodeAsync(Guid projectId, Guid serviceId, CancellationToken ct = default)
    {
        var ns = await OwnedAsync(projectId, serviceId, "service", ct);
        var maps = await client.CoreV1.ListNamespacedConfigMapAsync(ns.Metadata.Name, fieldSelector: "metadata.name=" + SourceName, cancellationToken: ct);
        var map = maps.Items.SingleOrDefault();
        return map is null ? null : JsonSerializer.Deserialize<ServiceCode>(map.Data["definition"]);
    }

    public async Task UpdateCodeAsync(Guid projectId, Guid serviceId, ServiceCode code, CancellationToken ct = default)
    {
        var ns = await OwnedAsync(projectId, serviceId, "service", ct);
        ValidateCode(code, options.Value);
        var existing = await client.CoreV1.ReadNamespacedConfigMapAsync(SourceName, ns.Metadata.Name, cancellationToken: ct);
        var deployment = await client.AppsV1.ReadNamespacedDeploymentAsync("app", ns.Metadata.Name, cancellationToken: ct);
        var map = BuildSourceMap(code);
        map.Metadata.ResourceVersion = existing.Metadata.ResourceVersion;
        ApplySource(deployment, code, options.Value);
        var policy = await client.NetworkingV1.ReadNamespacedNetworkPolicyAsync("workload-isolation", ns.Metadata.Name, cancellationToken: ct);
        policy.Spec.Egress = policy.Spec.Egress.Where(rule => !rule.To.Any(peer => peer.IpBlock is not null)).ToList();
        if (code.AllowNetworkEgress)
        {
            foreach (var rule in ClusterWorkloadRunner.BuildEgressPolicy("", "", true).Spec.Egress.Where(rule => rule.To.Any(peer => peer.IpBlock is not null)))
            {
                policy.Spec.Egress.Add(rule);
            }
        }
        await client.CoreV1.ReplaceNamespacedConfigMapAsync(map, SourceName, ns.Metadata.Name, cancellationToken: ct);
        await client.NetworkingV1.ReplaceNamespacedNetworkPolicyAsync(policy, policy.Metadata.Name, ns.Metadata.Name, cancellationToken: ct);
        await client.AppsV1.ReplaceNamespacedDeploymentAsync(deployment, "app", ns.Metadata.Name, cancellationToken: ct);
    }
}
