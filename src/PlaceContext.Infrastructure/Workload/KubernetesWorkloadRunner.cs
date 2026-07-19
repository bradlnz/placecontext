using System.Collections.Concurrent;
using System.Net;
using System.Text;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging;
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
///
/// <para><b>Jobs never run as root.</b> The run container carries a restricted securityContext
/// (nobody, no privilege escalation, all capabilities dropped), the pod a RuntimeDefault seccomp
/// profile and an fsGroup that keeps /work and /out writable for it. The materialize init container
/// stays root — it runs only our fixed copy script and opens /work up for the run container.</para>
///
/// <para><b>Warm dependency cache.</b> A code workload shipping a dependency manifest no longer installs
/// per pod: the first run bakes the layer once (a dedicated Job tars the installed deps and PUTs them to
/// the object store — see <see cref="BakeAsync"/>) and every later pod fetches + extracts the tar in its
/// init step, skipping the install via the <c>.baked</c> marker. Warmed pods get scoped egress to
/// MinIO + DNS instead of deny-all. Any warm-path failure falls back to the cold per-pod install.</para>
/// </summary>
public sealed class KubernetesWorkloadRunner : IWorkloadRunner
{
    private readonly WorkloadRunnerOptions _options;
    private readonly IObjectStore? _store;
    private readonly ILogger<KubernetesWorkloadRunner>? _log;

    // One bake per dependency-layer key (in-process); a failed bake is forgotten so the next run retries.
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _bakes = new();
    private const int BakeTimeoutSeconds = 600;
    private const string BakeUploaderImage = "curlimages/curl:8.10.1";

    public KubernetesWorkloadRunner(IOptions<WorkloadRunnerOptions> options, IObjectStore? store = null,
        ILogger<KubernetesWorkloadRunner>? log = null)
        => (_options, _store, _log) = (options.Value, store, log);

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
        string? depsGetUrl = null;   // presigned GET for the baked dependency layer (warm cache)
        var storeScopedEgress = false; // warm-cache pods get MinIO+DNS egress instead of deny-all
        if (request.CodeFiles is not null)
        {
            var runtimeId = request.RuntimeId ?? throw new InvalidOperationException("RuntimeId is required for code workloads.");
            if (!_options.Runtimes.TryGetValue(runtimeId, out var rt) || string.IsNullOrWhiteSpace(rt.BaseImage))
                throw new InvalidOperationException($"Unknown runtime '{runtimeId}'.");
            var entrypoint = !string.IsNullOrWhiteSpace(request.Entrypoint) ? request.Entrypoint! : rt.DefaultEntrypoint;
            image = rt.BaseImage;
            var invoke = string.Join(" ", rt.InvokeCommand.Select(s => ShQuote(s.Replace("{entrypoint}", entrypoint))));

            // A job shipping its runtime's dependency manifest (requirements.txt, package.json,
            // Gemfile, go.mod) gets its packages installed first — unless the baked layer already
            // exists in the object store: then the init container fetches it and the preamble's
            // .baked check skips the install. Downloads (cold path) still need AllowNetworkEgress.
            var depsPreamble = "";
            if (WorkloadDependencies.For(runtimeId, request.CodeFiles) is { } recipe)
            {
                depsPreamble = WorkloadDependencies.ShellPreamble(recipe);
                if (recipe.InvokePrefix is not null) invoke = recipe.InvokePrefix + " " + invoke;
                if (_options.WarmDependencyCache && _store is { IsEnabled: true })
                {
                    storeScopedEgress = false; // the fetch/upload needs MinIO + DNS even for no-egress jobs
                    depsGetUrl = await EnsureWarmCacheAsync(ns, client, runtimeId, image, recipe, request, ct);
                }
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
        var (data, script) = BuildMaterialize(request, NormalizeFiles(request), depsGetUrl is not null);

        // The ConfigMap and the egress policy are independent — create them concurrently. Both must
        // exist before the Job below, so this is awaited before the Job create. Sandbox: deny-all
        // egress unless the job opted in; a warm-cache pod instead gets SCOPED egress (MinIO + DNS
        // only) so it can fetch its baked dependency layer.
        var createConfigMap = client.CoreV1.CreateNamespacedConfigMapAsync(
            new V1ConfigMap { Metadata = new V1ObjectMeta { Name = name }, Data = data }, ns, cancellationToken: ct);
        var createNetPolicy = request.AllowNetworkEgress
            ? Task.CompletedTask
            : client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(
                BuildEgressPolicy(name, runLabel, storeScopedEgress), ns, cancellationToken: ct);
        await Task.WhenAll(createConfigMap, createNetPolicy);

        // ── The Job ─────────────────────────────────────────────────────────────────────────────
        var runContainer = new V1Container
        {
            Name = "run",
            Image = image,
            // A writable HOME for tools that insist on one (npm cache, go env); listed first so the
            // job's own env can still override it.
            Env = new[] { new V1EnvVar { Name = "HOME", Value = "/tmp" } }
                .Concat(request.Env.Select(kv => new V1EnvVar { Name = kv.Key, Value = kv.Value })).ToList(),
            VolumeMounts = new[]
            {
                new V1VolumeMount { Name = "work", MountPath = "/work" },
                // /out must be a volume: running as nobody, the job could never mkdir /out on the
                // root-owned image rootfs. The pod fsGroup makes the emptyDir group-writable.
                new V1VolumeMount { Name = "out", MountPath = "/out" },
            },
            Resources = ResourceLimits(),
            SecurityContext = BuildRestrictedContainerSecurityContext(_options),
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
                        // Jobs never run as root: pod-level seccomp + an fsGroup that keeps /work and
                        // /out writable for the unprivileged run container; the run container itself
                        // adds the uid/gid, no-new-privileges and dropped capabilities. The
                        // materialize init container stays root — it runs only our fixed copy script
                        // and must reach arbitrary artifact-mount paths.
                        SecurityContext = BuildPodSecurityContext(_options),
                        // Keep execution on the operator's own machines: prefer (or require) worker/agent
                        // nodes so the control-plane node — typically a small cloud server whose only duty
                        // is the portal/MCP — doesn't burn its CPU running job pods. See BuildWorkerAffinity.
                        Affinity = BuildWorkerAffinity(_options),
                        NodeSelector = _options.JobNodeSelector.Count > 0 ? _options.JobNodeSelector : null,
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
                                Command = new[] { "sh", "-c", script },
                                Env = depsGetUrl is null
                                    ? null
                                    : new[] { new V1EnvVar { Name = "PCDEPS_GET_URL", Value = depsGetUrl } },
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
                            new V1Volume { Name = "out", EmptyDir = new V1EmptyDirVolumeSource() },
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

    // ── Warm dependency cache (bake once per manifest hash, reuse from the object store) ──────────

    /// <summary>
    /// The ConfigMap payload + the busybox materialisation script: every file lands under /work,
    /// then — when a baked dependency layer exists for this workload — the tar is fetched and
    /// extracted over it (guarded: a fetch failure just means a cold, installing run).
    /// </summary>
    internal static (Dictionary<string, string> Data, string Script) BuildMaterialize(
        WorkloadRunRequest request, IReadOnlyList<(string Path, string Content)> files, bool fetchDeps)
    {
        var data = new Dictionary<string, string>();
        var script = new StringBuilder("set -e\n");
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
        if (fetchDeps)
            script.Append("wget -q -O- \"$PCDEPS_GET_URL\" | tar xz -C /work || true\n");
        // The run container executes as nobody: open the materialised tree (the init container's
        // copies are root-owned) so the job can write beside the code — node_modules, .bundle,
        // npm rewriting a lockfile, the .pcdeps deps root. Artifact mounts stay read-only.
        script.Append("chmod -R a+rwX /work\n");
        return (data, script.ToString());
    }

    /// <summary>
    /// Ensures the baked dependency layer for this workload exists in the object store and returns
    /// a presigned GET URL for it (null → the shard runs cold and installs). The first caller runs
    /// the bake Job; concurrent shards of the same layer share one bake.
    /// </summary>
    private Task<string?> EnsureWarmCacheAsync(string ns, Kubernetes client, string runtimeId, string baseImage,
        WorkloadDependencies.Recipe recipe, WorkloadRunRequest request, CancellationToken ct)
    {
        var key = $"{runtimeId}/{WorkloadDependencies.BakeKey(runtimeId, baseImage, recipe, request.CodeFiles!)}.tar.gz";
        var lazy = _bakes.GetOrAdd(key, k => new Lazy<Task<string?>>(
            () => BakeAsync(ns, client, k, runtimeId, baseImage, recipe, request, ct)));
        return AwaitBakeAsync(key, lazy);
    }

    private async Task<string?> AwaitBakeAsync(string key, Lazy<Task<string?>> lazy)
    {
        string? result;
        try { result = await lazy.Value; }
        catch { result = null; }
        if (result is null) _bakes.TryRemove(key, out _); // a failed bake is retried on the next run
        return result;
    }

    /// <summary>
    /// Runs the bake Job for one dependency layer: the runtime image installs the packages into a
    /// staging dir (mirroring a shard's /work layout), tars it, and a curl sidecar PUTs the tar to
    /// the object store. Never throws — any failure leaves the shard on the cold install path.
    /// </summary>
    private async Task<string?> BakeAsync(string ns, Kubernetes client, string key, string runtimeId,
        string baseImage, WorkloadDependencies.Recipe recipe, WorkloadRunRequest request, CancellationToken ct)
    {
        var store = _store!;
        var bucket = store.DepsBucket;
        var name = "pcbake-" + WorkloadDependencies.BakeKey(runtimeId, baseImage, recipe, request.CodeFiles!);
        var createdJob = false;
        try
        {
            await store.EnsureBucketAsync(bucket, ct);
            if (await store.ExistsAsync(bucket, key, ct))
                return await store.PresignDownloadAsync(bucket, key, TimeSpan.FromHours(1), ct);

            var manifests = WorkloadDependencies.ManifestFiles(recipe, request.CodeFiles!);
            var data = new Dictionary<string, string>();
            for (var i = 0; i < manifests.Count; i++) data[$"f{i}"] = manifests[i].Content;
            var putUrl = await store.PresignUploadAsync(bucket, key, TimeSpan.FromMinutes(15), ct);

            // Another replica may be baking the same layer right now — 409s are fine: we share
            // their Job and only clean up resources WE created (the TTL + active deadline bound
            // any leftovers from a crashed baker).
            try
            {
                await client.CoreV1.CreateNamespacedConfigMapAsync(
                    new V1ConfigMap { Metadata = new V1ObjectMeta { Name = name }, Data = data }, ns, cancellationToken: ct);
            }
            catch (HttpOperationException ex) when (ex.Response?.StatusCode == HttpStatusCode.Conflict) { }
            try
            {
                await client.BatchV1.CreateNamespacedJobAsync(
                    BuildBakeJob(name, baseImage, BuildBakeScript(recipe, manifests), putUrl, _options, BakeResourceLimits()),
                    ns, cancellationToken: ct);
                createdJob = true;
            }
            catch (HttpOperationException ex) when (ex.Response?.StatusCode == HttpStatusCode.Conflict) { }

            await WaitForJobCompletionAsync(client, ns, name, BakeTimeoutSeconds + 30, ct);
            // The object is the ground truth (not the Job status): another replica's bake may have
            // completed while its Job was already cleaned up under our poll.
            if (await store.ExistsAsync(bucket, key, ct))
            {
                _log?.LogInformation("Baked dependency layer {Bucket}/{Key} ({Runtime}).", bucket, key, runtimeId);
                return await store.PresignDownloadAsync(bucket, key, TimeSpan.FromHours(1), ct);
            }
            _log?.LogWarning("Dependency bake for {Key} did not produce a cache object; shards run cold.", key);
            return null;
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Dependency bake for {Key} failed; shards run cold.", key);
            return null;
        }
        finally
        {
            // No NetworkPolicy is created for bake jobs — the bake container needs full internet
            // egress to install packages (pip install, npm install, etc.).
            if (createdJob) await CleanupAsync(client, ns, name, hadEgress: true);
        }
    }

    /// <summary>Polls a Job to a terminal state (or the deadline). False on failure/timeout/disappearance.</summary>
    private static async Task<bool> WaitForJobCompletionAsync(Kubernetes client, string ns, string name,
        int timeoutSeconds, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var job = await client.BatchV1.ReadNamespacedJobStatusAsync(name, ns, cancellationToken: ct);
                if ((job.Status?.Succeeded ?? 0) >= 1) return true;
                if ((job.Status?.Failed ?? 0) >= 1) return false;
            }
            catch (HttpOperationException ex) when (ex.Response?.StatusCode == HttpStatusCode.NotFound)
            {
                return false; // deleted under us (the baking replica cleaned up) — treat as failed
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
        return false;
    }

    /// <summary>
    /// The bake container's script: materialise the manifests into the staging dir, run the same
    /// env + install a shard would (with /work → /stage so the tar mirrors a shard's filesystem
    /// exactly), then mark and pack the layer. The uploader sidecar waits for /out/.done.
    /// </summary>
    internal static string BuildBakeScript(WorkloadDependencies.Recipe recipe,
        IReadOnlyList<(string Path, string Content)> manifests)
    {
        var sb = new StringBuilder("set -e\n");
        sb.Append("mkdir -p /stage/.pcdeps /out\n");
        for (var i = 0; i < manifests.Count; i++)
            sb.Append($"cp /cm/f{i} {ShQuote("/stage/" + manifests[i].Path)}\n");
        sb.Append(WorkloadDependencies.Apply(recipe.EnvTemplate, "/stage", "/stage/.pcdeps")).Append('\n');
        sb.Append(WorkloadDependencies.Apply(recipe.BakeInstall, "/stage", "/stage/.pcdeps")).Append('\n');
        sb.Append("touch /stage/.baked\n");
        sb.Append("tar czf /out/deps.tar.gz -C /stage .\n");
        sb.Append("touch /out/.done\n");
        return sb.ToString();
    }

    /// <summary>
    /// The two-container bake Job: <c>bake</c> (the workload's runtime image) installs + tars into a
    /// shared emptyDir; <c>upload</c> (a curl image) waits for the marker and PUTs the tarball to
    /// the presigned URL. The pod carries the same worker-node placement, resource limits and
    /// non-root security context as job pods — the bake executes the same untrusted install recipes
    /// (its emptyDirs stay writable via the pod fsGroup); the upload sidecar only reads them. Its
    /// NetworkPolicy (scoped MinIO+DNS egress) is created alongside.
    /// </summary>
    internal static V1Job BuildBakeJob(string name, string image, string bakeScript, string putUrl,
        WorkloadRunnerOptions options, V1ResourceRequirements resources)
        => new()
        {
            Metadata = new V1ObjectMeta { Name = name, Labels = new Dictionary<string, string> { ["placecontext-run"] = name } },
            Spec = new V1JobSpec
            {
                BackoffLimit = 0,
                ActiveDeadlineSeconds = BakeTimeoutSeconds,
                TtlSecondsAfterFinished = 600,
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta { Labels = new Dictionary<string, string> { ["placecontext-run"] = name } },
                    Spec = new V1PodSpec
                    {
                        RestartPolicy = "Never",
                        SecurityContext = BuildPodSecurityContext(options),
                        Affinity = BuildWorkerAffinity(options),
                        NodeSelector = options.JobNodeSelector.Count > 0 ? options.JobNodeSelector : null,
                        Containers = new[]
                        {
                            new V1Container
                            {
                                Name = "bake",
                                Image = image,
                                Command = new[] { "sh", "-c", bakeScript },
                                Resources = resources,
                                SecurityContext = BuildRestrictedContainerSecurityContext(options),
                                VolumeMounts = new[]
                                {
                                    new V1VolumeMount { Name = "cm", MountPath = "/cm", ReadOnlyProperty = true },
                                    new V1VolumeMount { Name = "stage", MountPath = "/stage" },
                                    new V1VolumeMount { Name = "out", MountPath = "/out" },
                                },
                            },
                            new V1Container
                            {
                                Name = "upload",
                                Image = BakeUploaderImage,
                                Command = new[] { "sh", "-c",
                                    "while [ ! -f /out/.done ]; do sleep 1; done\n" +
                                    "curl -fsS -X PUT --upload-file /out/deps.tar.gz \"$PCDEPS_PUT_URL\"\n" },
                                Env = new[] { new V1EnvVar { Name = "PCDEPS_PUT_URL", Value = putUrl } },
                                VolumeMounts = new[] { new V1VolumeMount { Name = "out", MountPath = "/out" } },
                            },
                        },
                        Volumes = new[]
                        {
                            new V1Volume { Name = "cm", ConfigMap = new V1ConfigMapVolumeSource { Name = name } },
                            new V1Volume { Name = "stage", EmptyDir = new V1EmptyDirVolumeSource() },
                            new V1Volume { Name = "out", EmptyDir = new V1EmptyDirVolumeSource() },
                        },
                    },
                },
            },
        };

    /// <summary>
    /// The pod-level security context every workload pod carries: the default seccomp profile, plus
    /// an fsGroup matching <see cref="WorkloadRunnerOptions.RunAsGroup"/> so the emptyDir volumes
    /// (/work, /out, the bake's /stage) are group-writable by the unprivileged containers. runAs*
    /// deliberately live on the containers instead — the materialize init container must stay root
    /// to reach arbitrary artifact-mount paths.
    /// </summary>
    internal static V1PodSecurityContext BuildPodSecurityContext(WorkloadRunnerOptions options)
        => new()
        {
            SeccompProfile = new V1SeccompProfile { Type = "RuntimeDefault" },
            FsGroup = options.RunAsGroup,
        };

    /// <summary>
    /// The restricted container-level security context for the containers that execute workload code
    /// (the run container, the dependency bake): non-root numeric identity, no privilege escalation,
    /// all capabilities dropped. RunAsNonRoot follows the configured uid so an operator who knowingly
    /// sets RunAsUser=0 doesn't produce pods the kubelet refuses to start.
    /// </summary>
    internal static V1SecurityContext BuildRestrictedContainerSecurityContext(WorkloadRunnerOptions options)
        => new()
        {
            RunAsNonRoot = options.RunAsUser != 0,
            RunAsUser = options.RunAsUser,
            RunAsGroup = options.RunAsGroup,
            AllowPrivilegeEscalation = false,
            Capabilities = new V1Capabilities { Drop = new[] { "ALL" } },
        };

    /// <summary>
    /// The egress policy for a workload pod. Default is deny-all (the sealed sandbox). Pods on the
    /// warm-cache path instead get <em>scoped</em> egress — MinIO (to fetch/PUT the baked layer)
    /// plus DNS (to resolve it): the minimum opening that lets the cache work. Pods whose job
    /// opted into network egress get no policy at all.
    /// </summary>
    internal static V1NetworkPolicy BuildEgressPolicy(string name, string runLabel, bool storeScoped)
    {
        var egress = storeScoped
            ? new List<V1NetworkPolicyEgressRule>
            {
                new()
                {
                    To = new List<V1NetworkPolicyPeer>
                    {
                        new() { PodSelector = new V1LabelSelector { MatchLabels = new Dictionary<string, string> { ["app"] = "minio" } } },
                    },
                    Ports = new List<V1NetworkPolicyPort> { new() { Port = 9000, Protocol = "TCP" } },
                },
                new()
                {
                    Ports = new List<V1NetworkPolicyPort>
                    {
                        new() { Port = 53, Protocol = "UDP" },
                        new() { Port = 53, Protocol = "TCP" },
                    },
                },
            }
            : new List<V1NetworkPolicyEgressRule>(); // empty ⇒ deny all egress
        return new V1NetworkPolicy
        {
            Metadata = new V1ObjectMeta { Name = name },
            Spec = new V1NetworkPolicySpec
            {
                PodSelector = new V1LabelSelector { MatchLabels = new Dictionary<string, string> { ["placecontext-run"] = runLabel } },
                PolicyTypes = new[] { "Egress" },
                Egress = egress,
            },
        };
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

    /// <summary>
    /// Node affinity steering job pods onto worker (agent) nodes — any node without the
    /// <c>node-role.kubernetes.io/control-plane</c> label, which k3s stamps on every server node.
    /// <see cref="WorkloadRunnerOptions.PreferWorkerNodes"/> (default) is a soft preference, so a
    /// single-node install still runs jobs; <see cref="WorkloadRunnerOptions.RequireWorkerNodes"/>
    /// hardens it so job pods never land on the portal server. Null when both are off.
    /// </summary>
    public static V1Affinity? BuildWorkerAffinity(WorkloadRunnerOptions options)
    {
        if (!options.PreferWorkerNodes && !options.RequireWorkerNodes) return null;

        var notControlPlane = new V1NodeSelectorTerm
        {
            MatchExpressions = new List<V1NodeSelectorRequirement>
            {
                new()
                {
                    Key = "node-role.kubernetes.io/control-plane",
                    OperatorProperty = "DoesNotExist",
                },
            },
        };

        if (options.RequireWorkerNodes)
            return new V1Affinity
            {
                NodeAffinity = new V1NodeAffinity
                {
                    RequiredDuringSchedulingIgnoredDuringExecution = new V1NodeSelector
                    {
                        NodeSelectorTerms = new List<V1NodeSelectorTerm> { notControlPlane },
                    },
                },
            };

        return new V1Affinity
        {
            NodeAffinity = new V1NodeAffinity
            {
                PreferredDuringSchedulingIgnoredDuringExecution = new List<V1PreferredSchedulingTerm>
                {
                    new() { Weight = 100, Preference = notControlPlane },
                },
            },
        };
    }

    private V1ResourceRequirements ResourceLimits() => BuildResourceLimits(_options.SandboxMemory, _options.SandboxCpus);

    /// <summary>
    /// Bake containers run untrusted install scripts (pip install, npm install) that are far more
    /// memory-hungry than just executing code. Double the default memory limit to avoid OOMKills
    /// during heavy dependency installs; keep the same CPU cap.
    /// </summary>
    private V1ResourceRequirements BakeResourceLimits()
    {
        var mem = _options.SandboxMemory;
        if (!string.IsNullOrWhiteSpace(mem))
        {
            var num = int.TryParse(new string(mem.Where(char.IsDigit).ToArray()), out var n) ? n : 0;
            if (num > 0)
            {
                var suffix = new string(mem.Where(c => !char.IsDigit(c)).ToArray());
                mem = (num * 2) + suffix;
            }
        }
        return BuildResourceLimits(mem, _options.SandboxCpus);
    }

    private static V1ResourceRequirements BuildResourceLimits(string memory, double cpus)
    {
        var limits = new Dictionary<string, ResourceQuantity>();
        if (!string.IsNullOrWhiteSpace(memory)) limits["memory"] = new ResourceQuantity(ToK8sMemory(memory));
        if (cpus > 0) limits["cpu"] = new ResourceQuantity(cpus.ToString("0.0"));
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
