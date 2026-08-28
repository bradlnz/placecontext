using k8s.Models;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// Unit tests for <see cref="KubernetesWorkloadRunner"/> helpers that don't need a live cluster.
/// </summary>
public class KubernetesWorkloadRunnerTests
{
    // Regression: the shared SandboxMemory option is written in Docker notation ("256m" = 256 MiB),
    // but a bare "m" in a Kubernetes quantity means milli (10⁻³). Passing it verbatim set the
    // container memory limit to ~0 bytes, so runc OOM-killed the container init before any user code
    // ran (exit 128, StartError, empty logs) — every in-cluster job failed. ToK8sMemory translates it.
    [Theory]
    [InlineData("256m", "256Mi")]   // Docker MiB → k8s MiB (the bug that broke all jobs)
    [InlineData("512m", "512Mi")]
    [InlineData("1g", "1Gi")]       // Docker GiB → k8s GiB
    [InlineData("64k", "64Ki")]     // Docker KiB → k8s KiB
    [InlineData("1024b", "1024")]   // explicit bytes → bare number
    [InlineData("268435456", "268435456")] // bare bytes → unchanged
    [InlineData("256Mi", "256Mi")]  // already a k8s suffix → unchanged
    [InlineData("2Gi", "2Gi")]
    [InlineData("", "")]            // empty → unchanged (limit omitted upstream)
    public void ToK8sMemory_translates_docker_notation(string input, string expected)
        => Assert.Equal(expected, KubernetesWorkloadRunner.ToK8sMemory(input));

    // ── framed /out capture (SplitFramedLogs) ─────────────────────────────────────────────────────
    // Frames are base64 (v2): header '==PC-FILE== name rawByteCount', payload lines, then the end
    // marker. Base64 keeps the pod-log channel ASCII-safe so binary files survive byte-exact.

    private static string Frame(string name, byte[] bytes, int wrapAt = 76)
    {
        var b64 = Convert.ToBase64String(bytes);
        var wrapped = string.Join("\n", b64.Chunk(wrapAt).Select(c => new string(c)));
        return $"==PC-FILE== {name} {bytes.Length}\n{wrapped}\n{KubernetesWorkloadRunner.FileEndMarker}\n";
    }

    [Fact]
    public void Framed_logs_split_into_stdout_and_named_files()
    {
        var logs = "{\"ok\":true}\n" + KubernetesWorkloadRunner.ArtifactsMarker + "\n" +
                   Frame("listings.txt", System.Text.Encoding.UTF8.GetBytes("%PDF-1.4")) +
                   Frame("sub/data.csv", System.Text.Encoding.UTF8.GetBytes("a,b"));
        var (stdout, files) = KubernetesWorkloadRunner.SplitFramedLogs(logs);
        Assert.Equal("{\"ok\":true}", stdout);
        Assert.Equal(2, files.Count);
        Assert.Equal("listings.txt", files[0].Name);
        Assert.Equal("%PDF-1.4", files[0].Content);
        Assert.False(files[0].IsBinary);
        Assert.Equal("sub/data.csv", files[1].Name);
        Assert.Equal("a,b", files[1].Content);
    }

    [Fact]
    public void Binary_files_round_trip_byte_exact_as_base64_artifacts()
    {
        // A real PDF header includes bytes that are not valid UTF-8 — the exact content the old
        // text pipeline corrupted. It must come back byte-identical, flagged binary.
        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x00, 0xFF };
        var logs = "done\n" + KubernetesWorkloadRunner.ArtifactsMarker + "\n" + Frame("report.pdf", pdf, wrapAt: 8);
        var (_, files) = KubernetesWorkloadRunner.SplitFramedLogs(logs);
        var f = Assert.Single(files);
        Assert.Equal("report.pdf", f.Name);
        Assert.True(f.IsBinary);
        Assert.Equal(pdf, f.GetBytes());
    }

    [Fact]
    public void Payload_lines_that_look_like_frame_headers_cannot_occur_and_size_mismatches_are_dropped()
    {
        // Base64 payloads can never contain a header/end-marker line, so parsing is unambiguous.
        // A frame whose decoded size disagrees with its header (mid-payload log loss) is dropped.
        var good = System.Text.Encoding.UTF8.GetBytes("real content");
        var logs = "out\n" + KubernetesWorkloadRunner.ArtifactsMarker + "\n" +
                   $"==PC-FILE== bad.bin 9999\n{Convert.ToBase64String(good)}\n{KubernetesWorkloadRunner.FileEndMarker}\n" +
                   Frame("good.txt", good);
        var (_, files) = KubernetesWorkloadRunner.SplitFramedLogs(logs);
        var f = Assert.Single(files);
        Assert.Equal("good.txt", f.Name);
        Assert.Equal("real content", f.Content);
    }

    [Fact]
    public void Logs_without_a_marker_pass_through_unchanged_and_truncated_frames_are_dropped()
    {
        var (stdout, files) = KubernetesWorkloadRunner.SplitFramedLogs("plain output\n");
        Assert.Equal("plain output", stdout);
        Assert.Empty(files);

        // Log cut off before the end marker ⇒ the partial frame is dropped, stdout survives.
        var truncated = "x\n" + KubernetesWorkloadRunner.ArtifactsMarker + "\n==PC-FILE== f.txt 100\nc2hvcnQ=";
        var (s2, f2) = KubernetesWorkloadRunner.SplitFramedLogs(truncated);
        Assert.Equal("x", s2);
        Assert.Empty(f2);
    }

    [Fact]
    public void Empty_out_dir_yields_stdout_only()
    {
        var (stdout, files) = KubernetesWorkloadRunner.SplitFramedLogs(
            "result\n" + KubernetesWorkloadRunner.ArtifactsMarker + "\n");
        Assert.Equal("result", stdout);
        Assert.Empty(files);
    }

    // ── job placement (BuildWorkerAffinity) ───────────────────────────────────────────────────────
    // The portal server (k3s control plane, e.g. a cloud droplet) should serve the portal while job
    // pods execute on the operator's own machines (agents joined over Tailscale).

    [Fact]
    public void Default_placement_prefers_worker_nodes_but_never_blocks_a_run()
    {
        var affinity = KubernetesWorkloadRunner.BuildWorkerAffinity(new WorkloadRunnerOptions());

        Assert.NotNull(affinity);
        Assert.Null(affinity!.NodeAffinity.RequiredDuringSchedulingIgnoredDuringExecution);
        var term = Assert.Single(affinity.NodeAffinity.PreferredDuringSchedulingIgnoredDuringExecution);
        Assert.Equal(100, term.Weight);
        var expr = Assert.Single(term.Preference.MatchExpressions);
        Assert.Equal("node-role.kubernetes.io/control-plane", expr.Key);
        Assert.Equal("DoesNotExist", expr.OperatorProperty);
    }

    [Fact]
    public void Require_worker_nodes_hard_excludes_the_control_plane()
    {
        var affinity = KubernetesWorkloadRunner.BuildWorkerAffinity(
            new WorkloadRunnerOptions { RequireWorkerNodes = true });

        Assert.NotNull(affinity);
        Assert.Null(affinity!.NodeAffinity.PreferredDuringSchedulingIgnoredDuringExecution);
        var term = Assert.Single(affinity.NodeAffinity.RequiredDuringSchedulingIgnoredDuringExecution.NodeSelectorTerms);
        var expr = Assert.Single(term.MatchExpressions);
        Assert.Equal("node-role.kubernetes.io/control-plane", expr.Key);
        Assert.Equal("DoesNotExist", expr.OperatorProperty);
    }

    [Fact]
    public void Placement_affinity_can_be_disabled_entirely()
    {
        Assert.Null(KubernetesWorkloadRunner.BuildWorkerAffinity(
            new WorkloadRunnerOptions { PreferWorkerNodes = false, RequireWorkerNodes = false }));
    }

    // ── warm dependency cache (bake once, reuse from the object store) ────────────────────────────

    private static WorkloadRunRequest CodeRequest(params (string Path, string Content)[] files) =>
        new(Image: null, StdinPayload: "{}", Env: new Dictionary<string, string>(),
            ArtifactMounts: Array.Empty<(string, string)>(), CorrelationId: "corr",
            CodeFiles: files, RuntimeId: "python", Entrypoint: null);

    [Fact]
    public void Materialize_fetches_the_baked_layer_only_when_warm()
    {
        var files = new[] { ("main.py", "x") };
        var (_, cold) = KubernetesWorkloadRunner.BuildMaterialize(CodeRequest(files), files, fetchDeps: false);
        Assert.DoesNotContain("PCDEPS_GET_URL", cold);

        var (_, warm) = KubernetesWorkloadRunner.BuildMaterialize(CodeRequest(files), files, fetchDeps: true);
        Assert.Contains("wget -q -O- \"$PCDEPS_GET_URL\" | tar xz -C /work || true", warm);
    }

    [Fact]
    public void Bake_script_mirrors_the_shard_layout_then_marks_and_packs_the_layer()
    {
        var files = new[] { ("main.py", "x"), ("requirements.txt", "six") };
        var recipe = WorkloadDependencies.For("python", files)!;
        var script = KubernetesWorkloadRunner.BuildBakeScript(recipe, WorkloadDependencies.ManifestFiles(recipe, files));

        Assert.StartsWith("set -e\n", script);
        Assert.Contains("cp /cm/f0 '/stage/requirements.txt'", script);
        Assert.Contains("export PYTHONPATH=/stage/.pcdeps/lib", script);
        Assert.Contains("pip install --no-cache-dir --target /stage/.pcdeps/lib -r /stage/requirements.txt", script);
        Assert.Contains("touch /stage/.baked", script);           // the marker warm shards skip installs on
        Assert.Contains("tar czf /out/deps.tar.gz -C /stage .", script);
        Assert.EndsWith("touch /out/.done\n", script);            // releases the uploader sidecar
    }

    [Fact]
    public void Bake_job_installs_in_the_runtime_image_and_uploads_via_the_curl_sidecar()
    {
        var job = KubernetesWorkloadRunner.BuildBakeJob(
            "pcbake-abc", "python:3.12-slim", "echo bake", "http://minio:9000/signed-put",
            new WorkloadRunnerOptions(), new V1ResourceRequirements());

        Assert.Equal("pcbake-abc", job.Metadata.Name);
        Assert.Equal(600, job.Spec.TtlSecondsAfterFinished);
        Assert.False(job.Spec.Template.Spec.AutomountServiceAccountToken);
        var containers = job.Spec.Template.Spec.Containers;
        Assert.Equal(2, containers.Count);
        Assert.Equal("bake", containers[0].Name);
        Assert.Equal("python:3.12-slim", containers[0].Image);
        var upload = containers[1];
        Assert.Equal("upload", upload.Name);
        Assert.Contains("--upload-file /out/deps.tar.gz", upload.Command[2]);
        Assert.Equal("http://minio:9000/signed-put", Assert.Single(upload.Env).Value);
        Assert.True(upload.SecurityContext.RunAsNonRoot);
        Assert.False(upload.SecurityContext.AllowPrivilegeEscalation);
        Assert.Equal(3, job.Spec.Template.Spec.Volumes.Count); // cm + stage + out
    }

    [Fact]
    public void Egress_policy_denies_by_default_and_limits_opted_in_jobs_to_dns_and_public_ips()
    {
        var deny = KubernetesWorkloadRunner.BuildEgressPolicy("n", "lbl", allowInternet: false);
        Assert.Empty(deny.Spec.Egress);

        var internet = KubernetesWorkloadRunner.BuildEgressPolicy("n", "lbl", allowInternet: true);
        Assert.Equal(2, internet.Spec.Egress.Count);
        var dns = internet.Spec.Egress[0];
        Assert.Equal("kube-system", dns.To.Single().NamespaceSelector.MatchLabels["kubernetes.io/metadata.name"]);
        Assert.Equal("kube-dns", dns.To.Single().PodSelector.MatchLabels["k8s-app"]);
        Assert.Equal(2, dns.Ports.Count); // DNS over UDP + TCP
        var publicCidr = internet.Spec.Egress[1].To.Single().IpBlock;
        Assert.Equal("0.0.0.0/0", publicCidr.Cidr);
        Assert.Contains("10.0.0.0/8", publicCidr.Except);
        Assert.Contains("169.254.0.0/16", publicCidr.Except);
    }

    // ── non-root security context ───────────────────────────────────────────────────────────────
    // Job pods never run as root: pod-level seccomp + fsGroup (so the nobody run container can
    // write its volumes), container-level runAs*/capabilities on the containers that execute code.

    [Fact]
    public void Pod_security_context_applies_seccomp_and_the_run_as_group_as_fsgroup()
    {
        var pod = KubernetesWorkloadRunner.BuildPodSecurityContext(new WorkloadRunnerOptions());

        Assert.Equal("RuntimeDefault", pod.SeccompProfile.Type);
        Assert.Equal(65534, pod.FsGroup); // emptyDir volumes stay writable for the nobody container
    }

    [Fact]
    public void Container_security_context_runs_nobody_with_dropped_capabilities()
    {
        var ctr = KubernetesWorkloadRunner.BuildRestrictedContainerSecurityContext(new WorkloadRunnerOptions());

        Assert.True(ctr.RunAsNonRoot);
        Assert.Equal(65534, ctr.RunAsUser);
        Assert.Equal(65534, ctr.RunAsGroup);
        Assert.False(ctr.AllowPrivilegeEscalation);
        Assert.Equal("ALL", Assert.Single(ctr.Capabilities.Drop));
    }

    [Fact]
    public void Materializer_uses_the_same_restricted_non_root_context()
    {
        var ctr = KubernetesWorkloadRunner.BuildRestrictedContainerSecurityContext(new WorkloadRunnerOptions());

        Assert.True(ctr.RunAsNonRoot);
        Assert.Equal(65534, ctr.RunAsUser);
        Assert.False(ctr.AllowPrivilegeEscalation);
        Assert.Equal("ALL", Assert.Single(ctr.Capabilities.Drop));
    }

    [Fact]
    public void RunAsNonRoot_relaxes_only_when_the_operator_configures_uid_zero()
    {
        var ctr = KubernetesWorkloadRunner.BuildRestrictedContainerSecurityContext(
            new WorkloadRunnerOptions { RunAsUser = 0, RunAsGroup = 0 });

        // runAsNonRoot=true with a numeric uid 0 would make the kubelet refuse to start the pod.
        Assert.False(ctr.RunAsNonRoot);
        Assert.Equal(0, ctr.RunAsUser);
    }

    [Fact]
    public void Bake_job_runs_the_untrusted_install_as_nobody_too()
    {
        var job = KubernetesWorkloadRunner.BuildBakeJob(
            "pcbake-abc", "python:3.12-slim", "echo bake", "http://minio:9000/signed-put",
            new WorkloadRunnerOptions(), new V1ResourceRequirements());

        Assert.Equal("RuntimeDefault", job.Spec.Template.Spec.SecurityContext.SeccompProfile.Type);
        Assert.Equal(65534, job.Spec.Template.Spec.SecurityContext.FsGroup); // /stage + /out stay writable
        var bake = job.Spec.Template.Spec.Containers[0];
        Assert.True(bake.SecurityContext.RunAsNonRoot);
        Assert.Equal(65534, bake.SecurityContext.RunAsUser);
        Assert.False(bake.SecurityContext.AllowPrivilegeEscalation);
        Assert.Equal("ALL", Assert.Single(bake.SecurityContext.Capabilities.Drop));
    }

    [Fact]
    public void Materialize_opens_work_for_the_unprivileged_run_container()
    {
        var files = new[] { ("main.py", "x") };
        var (_, script) = KubernetesWorkloadRunner.BuildMaterialize(CodeRequest(files), files, fetchDeps: false);

        // Materializer and workload use the same uid; no root chmod should be needed.
        Assert.DoesNotContain("chmod", script);
    }

    [Fact]
    public void Materializer_rejects_artifact_paths_outside_the_shared_input_volume()
    {
        var request = CodeRequest(("main.py", "x")) with
        {
            ArtifactMounts = new[] { ("content", "/tmp/result.json") },
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            KubernetesWorkloadRunner.BuildMaterialize(request, Array.Empty<(string, string)>(), fetchDeps: false));

        Assert.Contains("under /in", error.Message);
    }

    // ── always-on runtime sandbox profiles (dotnet, go) ──────────────────────────────────────────
    // Parity with the Docker runner: the profile's env (with {deps} → the writable emptyDir) and
    // memory override apply in-cluster too. The exec tmpfs has no k8s counterpart — /work already
    // is one (emptyDir), which is where the profile points the caches.

    [Fact]
    public void Dotnet_container_env_relocates_the_toolchain_dirs_into_the_work_emptydir()
    {
        var opts = new WorkloadRunnerOptions();
        var request = CodeRequest(("main.cs", "x")) with { RuntimeId = "dotnet" };
        var env = KubernetesWorkloadRunner.BuildContainerEnv(opts.Runtimes["dotnet"], request);

        Assert.Equal("/work/.pcdeps/home", env.Single(e => e.Name == "HOME").Value);
        Assert.Equal("/work/.pcdeps/nuget", env.Single(e => e.Name == "NUGET_PACKAGES").Value);
        Assert.Equal("/work/.pcdeps/xdg", env.Single(e => e.Name == "XDG_DATA_HOME").Value);
        Assert.Equal("/work/.pcdeps/tmp", env.Single(e => e.Name == "TMPDIR").Value);
    }

    [Fact]
    public void Job_env_overrides_the_runtime_profile_in_cluster_too()
    {
        var opts = new WorkloadRunnerOptions();
        var request = CodeRequest(("main.cs", "x")) with
        {
            RuntimeId = "dotnet",
            Env = new Dictionary<string, string> { ["HOME"] = "/custom" },
        };
        var env = KubernetesWorkloadRunner.BuildContainerEnv(opts.Runtimes["dotnet"], request);

        Assert.Equal("/custom", env.Single(e => e.Name == "HOME").Value);
    }

    [Fact]
    public void Runtime_profile_memory_overrides_the_global_limit()
    {
        var opts = new WorkloadRunnerOptions();
        var sut = new KubernetesWorkloadRunner(Microsoft.Extensions.Options.Options.Create(opts));

        var dotnet = sut.ResourceLimits(opts.Runtimes["dotnet"]);
        Assert.Equal("1Gi", dotnet.Limits["memory"].CanonicalizeString());

        var @default = sut.ResourceLimits(opts.Runtimes["python"]);
        Assert.Equal("256Mi", @default.Limits["memory"].CanonicalizeString());
    }
}
