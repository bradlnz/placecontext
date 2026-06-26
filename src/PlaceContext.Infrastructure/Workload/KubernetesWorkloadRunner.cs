using System.Text;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Workload;

/// <summary>
/// Infrastructure adapter that runs a workload as a Kubernetes <b>Job</b> in the cluster the Host runs
/// in — used when PlaceContext is deployed in-cluster (no Docker socket). The Host's ServiceAccount must
/// have RBAC to manage Jobs/Pods/ConfigMaps/NetworkPolicies (see deploy/k3s/placecontext.yaml).
///
/// Per run it: writes the code file set + the shard payload into a ConfigMap, materialises them into an
/// emptyDir at /work via a busybox init container, then runs the runtime image which pipes
/// /work/input.json into the entrypoint on STDIN (preserving the same contract as the Docker runner).
/// The pod's stdout is captured as the artifact; the container exit code is the result. When network
/// egress is not allowed a deny-all-egress NetworkPolicy is attached to the pod (k3s enforces it).
/// </summary>
public sealed class KubernetesWorkloadRunner : IWorkloadRunner
{
    private readonly WorkloadRunnerOptions _options;

    public KubernetesWorkloadRunner(IOptions<WorkloadRunnerOptions> options) => _options = options.Value;

    public async Task<WorkloadRunResult> RunAsync(WorkloadRunRequest request, CancellationToken ct = default)
    {
        var cfg = KubernetesClientConfiguration.InClusterConfig();
        var ns = string.IsNullOrWhiteSpace(cfg.Namespace) ? "placecontext" : cfg.Namespace;
        using var client = new Kubernetes(cfg);

        var name = MakeName(request.CorrelationId);
        var runLabel = name; // pod selector for the NetworkPolicy

        // ── Resolve image + the in-pod run command ────────────────────────────────────────────────
        string image;
        string runShell; // shell run inside the pod (pipes the payload into the program on stdin)
        if (request.CodeFiles is not null)
        {
            var runtimeId = request.RuntimeId ?? throw new InvalidOperationException("RuntimeId is required for code workloads.");
            if (!_options.Runtimes.TryGetValue(runtimeId, out var rt) || string.IsNullOrWhiteSpace(rt.BaseImage))
                throw new InvalidOperationException($"Unknown runtime '{runtimeId}'.");
            var entrypoint = !string.IsNullOrWhiteSpace(request.Entrypoint) ? request.Entrypoint! : rt.DefaultEntrypoint;
            image = rt.BaseImage;
            var invoke = string.Join(" ", rt.InvokeCommand.Select(s => ShQuote(s.Replace("{entrypoint}", entrypoint))));
            runShell = "cat /work/input.json | " + invoke;
        }
        else
        {
            image = request.Image ?? throw new InvalidOperationException("Either Image or CodeFiles must be provided.");
            runShell = ""; // image workloads use their own entrypoint (no stdin injection in-cluster)
        }

        // ── ConfigMap: code files + payload + reduce artifact mounts, plus a materialisation script ──
        var data = new Dictionary<string, string>();
        var script = new StringBuilder("set -e\n");
        var files = NormalizeFiles(request);
        for (var i = 0; i < files.Count; i++)
        {
            var key = $"f{i}";
            data[key] = files[i].Content;
            script.Append($"mkdir -p \"/work/$(dirname {ShQuote(files[i].Path)})\"\ncp /cm/{key} {ShQuote("/work/" + files[i].Path)}\n");
        }
        data["input"] = request.StdinPayload ?? "";
        script.Append("cp /cm/input /work/input.json\n");
        for (var i = 0; i < request.ArtifactMounts.Count; i++)
        {
            var key = $"am{i}";
            data[key] = request.ArtifactMounts[i].Content;
            var p = request.ArtifactMounts[i].ContainerPath;
            script.Append($"mkdir -p \"$(dirname {ShQuote(p)})\"\ncp /cm/{key} {ShQuote(p)}\n");
        }

        await client.CoreV1.CreateNamespacedConfigMapAsync(
            new V1ConfigMap { Metadata = new V1ObjectMeta { Name = name }, Data = data }, ns, cancellationToken: ct);

        // ── Sealed sandbox: deny all egress unless the job opted in ─────────────────────────────────
        if (!request.AllowNetworkEgress)
        {
            await client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(new V1NetworkPolicy
            {
                Metadata = new V1ObjectMeta { Name = name },
                Spec = new V1NetworkPolicySpec
                {
                    PodSelector = new V1LabelSelector { MatchLabels = new Dictionary<string, string> { ["placecontext-run"] = runLabel } },
                    PolicyTypes = new[] { "Egress" },
                    Egress = new List<V1NetworkPolicyEgressRule>(), // empty ⇒ deny all egress
                },
            }, ns, cancellationToken: ct);
        }

        // ── The Job ─────────────────────────────────────────────────────────────────────────────
        var runContainer = new V1Container
        {
            Name = "run",
            Image = image,
            Env = request.Env.Select(kv => new V1EnvVar { Name = kv.Key, Value = kv.Value }).ToList(),
            VolumeMounts = new[] { new V1VolumeMount { Name = "work", MountPath = "/work" } },
            Resources = ResourceLimits(),
        };
        if (runShell.Length > 0) runContainer.Command = new[] { "sh", "-c", runShell };

        var job = new V1Job
        {
            Metadata = new V1ObjectMeta { Name = name, Labels = new Dictionary<string, string> { ["placecontext-run"] = runLabel } },
            Spec = new V1JobSpec
            {
                BackoffLimit = 0,
                ActiveDeadlineSeconds = _options.DefaultTimeoutSeconds,
                TtlSecondsAfterFinished = 120,
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta
                    {
                        // placecontext-run is unique per run (NetworkPolicy selector); placecontext-workload is the
                        // shared label every job pod carries so the scheduler can balance them across nodes.
                        Labels = new Dictionary<string, string>
                        {
                            ["placecontext-run"] = runLabel,
                            ["placecontext-workload"] = "job",
                        },
                    },
                    Spec = new V1PodSpec
                    {
                        RestartPolicy = "Never",
                        // Spread job pods evenly across nodes by hostname. ScheduleAnyway keeps it a soft
                        // preference (never blocks a run), but a freshly added node — which carries zero job
                        // pods — becomes the lowest-skew target, so new workload flows onto it automatically.
                        TopologySpreadConstraints = new[]
                        {
                            new V1TopologySpreadConstraint
                            {
                                MaxSkew = 1,
                                TopologyKey = "kubernetes.io/hostname",
                                WhenUnsatisfiable = "ScheduleAnyway",
                                LabelSelector = new V1LabelSelector
                                {
                                    MatchLabels = new Dictionary<string, string> { ["placecontext-workload"] = "job" },
                                },
                            },
                        },
                        InitContainers = new[]
                        {
                            new V1Container
                            {
                                Name = "materialize",
                                Image = "busybox:1.36",
                                Command = new[] { "sh", "-c", script.ToString() },
                                VolumeMounts = new[]
                                {
                                    new V1VolumeMount { Name = "cm", MountPath = "/cm", ReadOnlyProperty = true },
                                    new V1VolumeMount { Name = "work", MountPath = "/work" },
                                },
                            },
                        },
                        Containers = new[] { runContainer },
                        Volumes = new[]
                        {
                            new V1Volume { Name = "cm", ConfigMap = new V1ConfigMapVolumeSource { Name = name } },
                            new V1Volume { Name = "work", EmptyDir = new V1EmptyDirVolumeSource() },
                        },
                    },
                },
            },
        };

        try
        {
            await client.BatchV1.CreateNamespacedJobAsync(job, ns, cancellationToken: ct);
            return await AwaitResultAsync(client, ns, name, ct);
        }
        finally
        {
            await CleanupAsync(client, ns, name, request.AllowNetworkEgress);
        }
    }

    // Poll the Job to completion (or the deadline), then read the pod's exit code + logs (the artifact).
    private async Task<WorkloadRunResult> AwaitResultAsync(Kubernetes client, string ns, string name, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(_options.DefaultTimeoutSeconds + 30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var job = await client.BatchV1.ReadNamespacedJobStatusAsync(name, ns, cancellationToken: ct);
            if ((job.Status?.Succeeded ?? 0) >= 1 || (job.Status?.Failed ?? 0) >= 1)
                break;
            await Task.Delay(1500, ct);
        }

        var pods = await client.CoreV1.ListNamespacedPodAsync(ns, labelSelector: "job-name=" + name, cancellationToken: ct);
        var pod = pods.Items.OrderByDescending(p => p.Metadata.CreationTimestamp).FirstOrDefault();
        if (pod is null)
            return new WorkloadRunResult(-1, null, "", "no pod was created for the job");

        var exit = pod.Status?.ContainerStatuses?.FirstOrDefault(c => c.Name == "run")?.State?.Terminated?.ExitCode ?? -1;

        var logs = "";
        try
        {
            await using var stream = await client.CoreV1.ReadNamespacedPodLogAsync(pod.Metadata.Name, ns, container: "run", cancellationToken: ct);
            using var sr = new StreamReader(stream);
            logs = (await sr.ReadToEndAsync(ct)).Trim();
        }
        catch { /* logs best-effort */ }

        // stdout is the artifact (the authoring contract); no separate stderr stream in-cluster.
        return new WorkloadRunResult(exit, string.IsNullOrEmpty(logs) ? null : logs, logs, "");
    }

    private static async Task CleanupAsync(Kubernetes client, string ns, string name, bool hadEgress)
    {
        var bg = new V1DeleteOptions { PropagationPolicy = "Background" };
        try { await client.BatchV1.DeleteNamespacedJobAsync(name, ns, body: bg); } catch { }
        try { await client.CoreV1.DeleteNamespacedConfigMapAsync(name, ns); } catch { }
        if (!hadEgress)
            try { await client.NetworkingV1.DeleteNamespacedNetworkPolicyAsync(name, ns); } catch { }
    }

    private V1ResourceRequirements ResourceLimits()
    {
        var limits = new Dictionary<string, ResourceQuantity>();
        if (!string.IsNullOrWhiteSpace(_options.SandboxMemory)) limits["memory"] = new ResourceQuantity(_options.SandboxMemory);
        if (_options.SandboxCpus > 0) limits["cpu"] = new ResourceQuantity(_options.SandboxCpus.ToString("0.0"));
        return new V1ResourceRequirements { Limits = limits.Count > 0 ? limits : null };
    }

    // Single file with no explicit entrypoint → write it at the runtime's default name; else each file at its path.
    private List<(string Path, string Content)> NormalizeFiles(WorkloadRunRequest request)
    {
        var files = new List<(string, string)>();
        if (request.CodeFiles is null) return files;
        if (string.IsNullOrWhiteSpace(request.Entrypoint) && request.CodeFiles.Count == 1
            && request.RuntimeId is { } rid && _options.Runtimes.TryGetValue(rid, out var rt))
        {
            files.Add((rt.DefaultEntrypoint, request.CodeFiles[0].Content));
            return files;
        }
        foreach (var (path, content) in request.CodeFiles) files.Add((path, content));
        return files;
    }

    private static string ShQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    private static string MakeName(string correlationId)
    {
        var sb = new StringBuilder("pcjob-");
        foreach (var c in correlationId.ToLowerInvariant())
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-') sb.Append(c);
        var s = sb.ToString();
        if (s.Length > 50) s = s[..50];
        return s.TrimEnd('-');
    }
}
