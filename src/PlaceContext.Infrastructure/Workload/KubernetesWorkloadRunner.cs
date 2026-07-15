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
        // Per-job timeout wins; fall back to the global default when the job didn't set one.
        var timeoutSeconds = request.TimeoutSeconds is > 0 ? request.TimeoutSeconds.Value : _options.DefaultTimeoutSeconds;

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

            // A job shipping its runtime's dependency manifest (requirements.txt, package.json,
            // Gemfile, go.mod) gets its packages installed first — /work is a writable emptyDir
            // here, so installs land beside the code. Downloads still need AllowNetworkEgress.
            var depsPreamble = "";
            if (WorkloadDependencies.For(runtimeId, request.CodeFiles) is { } recipe)
            {
                depsPreamble = WorkloadDependencies.ShellPreamble(recipe);
                if (recipe.InvokePrefix is not null) invoke = recipe.InvokePrefix + " " + invoke;
            }
            // After the program runs, stream every /out file through the pod log with base64 framing —
            // a completed pod's filesystem is gone, so the log is the only channel back, and the log
            // pipeline (CRI → kubelet → API → UTF-8 string decode) is not binary-safe: base64 keeps the
            // frames pure ASCII so PDFs and other binary files survive byte-exact. The header carries the
            // raw byte count for integrity; the program's exit code is preserved. See SplitFramedLogs.
            runShell =
                "mkdir -p /out\n" +
                depsPreamble +
                "cat /work/input.json | " + invoke + "\n" +
                "rc=$?\n" +
                $"echo\necho {ShQuote(ArtifactsMarker)}\n" +
                "find /out -type f 2>/dev/null | while read -r f; do\n" +
                "  printf '==PC-FILE== %s %s\\n' \"${f#/out/}\" \"$(wc -c < \"$f\" | tr -d ' \\t')\"\n" +
                $"  base64 < \"$f\"\n" +
                $"  echo {ShQuote(FileEndMarker)}\n" +
                "done\n" +
                "exit $rc";
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

        // The ConfigMap and (sealed sandbox: deny-all-egress unless the job opted in) NetworkPolicy
        // are independent — create them concurrently. Both must exist before the Job below, so this
        // is awaited before the Job create.
        var createConfigMap = client.CoreV1.CreateNamespacedConfigMapAsync(
            new V1ConfigMap { Metadata = new V1ObjectMeta { Name = name }, Data = data }, ns, cancellationToken: ct);
        var createNetPolicy = request.AllowNetworkEgress
            ? Task.CompletedTask
            : client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(new V1NetworkPolicy
            {
                Metadata = new V1ObjectMeta { Name = name },
                Spec = new V1NetworkPolicySpec
                {
                    PodSelector = new V1LabelSelector { MatchLabels = new Dictionary<string, string> { ["placecontext-run"] = runLabel } },
                    PolicyTypes = new[] { "Egress" },
                    Egress = new List<V1NetworkPolicyEgressRule>(), // empty ⇒ deny all egress
                },
            }, ns, cancellationToken: ct);
        await Task.WhenAll(createConfigMap, createNetPolicy);

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
                ActiveDeadlineSeconds = timeoutSeconds,
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
            return await AwaitResultAsync(client, ns, name, timeoutSeconds, ct);
        }
        finally
        {
            await CleanupAsync(client, ns, name, request.AllowNetworkEgress);
        }
    }

    // Poll the Job to completion (or the deadline), then read the pod's exit code + logs (the artifact).
    private async Task<WorkloadRunResult> AwaitResultAsync(Kubernetes client, string ns, string name, int timeoutSeconds, CancellationToken ct)
    {
        // Give the poller a small grace beyond the pod's own ActiveDeadlineSeconds so we observe the
        // Job's Failed status (deadline-exceeded) rather than timing out the poll first.
        // Poll adaptively: short jobs are the common case, so start fast (200ms) and back off toward
        // 1.5s for long runs — a quick shard completes in sub-second wall-clock instead of paying a
        // fixed 1.5s tick, without hammering the API server on runs that take minutes.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds + 30);
        var delay = TimeSpan.FromMilliseconds(200);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var job = await client.BatchV1.ReadNamespacedJobStatusAsync(name, ns, cancellationToken: ct);
            if ((job.Status?.Succeeded ?? 0) >= 1 || (job.Status?.Failed ?? 0) >= 1)
                break;
            await Task.Delay(delay, ct);
            if (delay < TimeSpan.FromMilliseconds(1500))
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 1.5, 1500));
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
            logs = await sr.ReadToEndAsync(ct);
        }
        catch { /* logs best-effort */ }

        // stdout is the artifact (the authoring contract); framed /out files ride behind the marker.
        var (stdout, files) = SplitFramedLogs(logs);
        return new WorkloadRunResult(exit, string.IsNullOrEmpty(stdout) ? null : stdout, stdout, "", files);
    }

    /// <summary>Marker line the in-pod wrapper prints between the program's stdout and its framed /out files.</summary>
    public const string ArtifactsMarker = "---PC-ARTIFACTS-v2---";

    /// <summary>Marker line the in-pod wrapper prints after each file's base64 payload.</summary>
    public const string FileEndMarker = "==PC-FILE-END==";

    /// <summary>
    /// Splits a pod log into the program's stdout and the framed /out files behind
    /// <see cref="ArtifactsMarker"/>. Frames are '==PC-FILE== name rawByteCount\n' + base64 payload
    /// (any line wrapping) + '<see cref="FileEndMarker"/>\n'. The payload being base64 makes frame
    /// markers unambiguous (they can never appear inside it), and the raw byte count catches truncated
    /// logs — a frame that fails to decode to exactly that many bytes is dropped rather than surfaced
    /// corrupt. Logs from image workloads (no wrapper, no marker) pass through unchanged.
    /// </summary>
    /// <summary>Max decoded artifact size accepted from a framed log (matches Docker /out capture).</summary>
    public const long MaxFramedArtifactBytes = 5L * 1024 * 1024;
    private const int MaxFramedArtifacts = 50;
    /// <summary>Max base64 payload chars collected for one frame (~4/3 of max bytes + slack).</summary>
    private const int MaxFramedBase64Chars = 8 * 1024 * 1024;

    public static (string Stdout, List<WorkloadArtifact> Files) SplitFramedLogs(string logs)
    {
        var files = new List<WorkloadArtifact>();
        if (string.IsNullOrEmpty(logs)) return ("", files);

        var markerAt = logs.StartsWith(ArtifactsMarker, StringComparison.Ordinal)
            ? 0
            : logs.IndexOf("\n" + ArtifactsMarker, StringComparison.Ordinal) is var i and >= 0 ? i + 1 : -1;
        if (markerAt < 0) return (logs.Trim(), files);

        var stdout = logs[..markerAt].Trim();
        var lines = logs[(markerAt + ArtifactsMarker.Length)..].Split('\n');
        for (var l = 0; l < lines.Length; l++)
        {
            if (files.Count >= MaxFramedArtifacts) break;
            var header = lines[l].TrimEnd('\r');
            if (!header.StartsWith("==PC-FILE== ", StringComparison.Ordinal)) continue;
            var sep = header.LastIndexOf(' ');
            // Reject negative / absurd size claims (integer overflow tricks, multi-GB claims).
            if (sep <= 12 || !long.TryParse(header[(sep + 1)..], out var size)
                || size < 0 || size > MaxFramedArtifactBytes) continue;
            var name = header[12..sep];

            // Collect the base64 payload up to the end marker; no marker ⇒ truncated log, drop the frame.
            var payload = new StringBuilder();
            var closed = false;
            while (++l < lines.Length)
            {
                var line = lines[l].TrimEnd('\r');
                if (line == FileEndMarker) { closed = true; break; }
                if (payload.Length + line.Length > MaxFramedBase64Chars) { closed = false; break; }
                payload.Append(line);
            }
            if (!closed) break;

            try
            {
                var bytes = Convert.FromBase64String(payload.ToString());
                if (bytes.LongLength == size && bytes.LongLength <= MaxFramedArtifactBytes)
                    files.Add(WorkloadArtifact.FromBytes(name, bytes));
            }
            catch (FormatException) { /* mangled frame — drop it rather than surface corrupt content */ }
        }
        return (stdout, files);
    }

    // The three deletes are independent — issue them concurrently so cleanup costs one API
    // round-trip, not three, on every shard/reduce invocation.
    private static Task CleanupAsync(Kubernetes client, string ns, string name, bool hadEgress)
    {
        var bg = new V1DeleteOptions { PropagationPolicy = "Background" };
        static async Task Best(Func<Task> delete) { try { await delete(); } catch { } }
        return Task.WhenAll(
            Best(() => client.BatchV1.DeleteNamespacedJobAsync(name, ns, body: bg)),
            Best(() => client.CoreV1.DeleteNamespacedConfigMapAsync(name, ns)),
            hadEgress
                ? Task.CompletedTask
                : Best(() => client.NetworkingV1.DeleteNamespacedNetworkPolicyAsync(name, ns)));
    }

    private V1ResourceRequirements ResourceLimits()
    {
        var limits = new Dictionary<string, ResourceQuantity>();
        if (!string.IsNullOrWhiteSpace(_options.SandboxMemory)) limits["memory"] = new ResourceQuantity(ToK8sMemory(_options.SandboxMemory));
        if (_options.SandboxCpus > 0) limits["cpu"] = new ResourceQuantity(_options.SandboxCpus.ToString("0.0"));
        return new V1ResourceRequirements { Limits = limits.Count > 0 ? limits : null };
    }

    /// <summary>
    /// Translate the shared <see cref="WorkloadRunnerOptions.SandboxMemory"/> value — written in
    /// <b>Docker</b> notation (e.g. "256m" = 256&#160;MiB, base-1024) — into a Kubernetes resource
    /// quantity. This is critical: in a k8s quantity a bare "m" suffix means <i>milli</i> (10⁻³), so
    /// passing Docker's "256m" verbatim sets the container memory limit to ~0 bytes and runc OOM-kills
    /// the container's init process before any user code runs (exit 128, StartError, no logs). Docker's
    /// b/k/m/g suffixes map to k8s bytes/Ki/Mi/Gi; a bare number is bytes; an explicit k8s suffix
    /// (Ki/Mi/Gi) is left untouched.
    /// </summary>
    public static string ToK8sMemory(string docker)
    {
        var s = docker.Trim();
        if (s.Length == 0) return s;
        return char.ToLowerInvariant(s[^1]) switch
        {
            'b' => s[..^1],          // explicit bytes
            'k' => s[..^1] + "Ki",
            'm' => s[..^1] + "Mi",
            'g' => s[..^1] + "Gi",
            _ => s,                  // bare bytes, or an already-k8s suffix like "Mi"/"Gi"
        };
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
