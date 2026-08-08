using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;

namespace PlaceContext.Jobs.Infrastructure.Workload;

/// <summary>
/// Infrastructure adapter: runs a single generic container via the Docker CLI and returns the
/// raw exit code, artifact, stdout, and stderr. All inputs and outputs are opaque — this adapter
/// has no knowledge of what the container does.
///
/// Contract (generic):
/// • Mounts a fresh temp dir as /out — the artifact file (configurable, default result.json) is the deliverable.
/// • Writes StdinPayload to the container's STDIN, then closes it (signals EOF).
/// • ImageWorkload path: runs the image directly.
/// • CodeWorkload path: resolves the base image from the runtime registry, materialises source to
///   a temp work dir, mounts it read-only at /work inside the container, and overrides CMD with
///   the runtime's invoke command template.
/// • Dependency manifests: the package layer is baked once into a reusable pcwarm-* image
///   (WarmImages, default on) — first run builds, later runs reuse. The cold fallback installs
///   per run into a scratch tmpfs, as before.
/// • For reduce containers: mounts each supplied ArtifactMount (content → containerPath) read-only.
/// • Sandbox defaults applied to every run (configurable): --user nobody + --cap-drop ALL +
///   no-new-privileges, --pids-limit, --memory, --cpus, --read-only + /tmp tmpfs, --network none
///   by default. Never string-interpolates user values into a shell — docker argv is built as an array.
/// • Captures exit code, stdout, and stderr. Cleans up temp dirs on completion.
/// • Timeout handled: returns synthetic exit code -1 result (not exception) on timeout.
/// </summary>
public sealed class DockerWorkloadRunner : IWorkloadRunner
{
    private readonly WorkloadRunnerOptions _options;
    private readonly ILogger<DockerWorkloadRunner>? _log;
    private readonly IWorkloadOutputBuffer? _output;

    // One builder per dependency-layer tag: a shard fan-out (same manifest) builds its warm image
    // once and every other shard reuses it within the same run. Different layers build in parallel.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _buildGates = new();
    private static readonly TimeSpan WarmBuildTimeout = TimeSpan.FromMinutes(10);

    public DockerWorkloadRunner(IOptions<WorkloadRunnerOptions> options, ILogger<DockerWorkloadRunner>? log = null,
        IWorkloadOutputBuffer? output = null)
        => (_options, _log, _output) = (options.Value, log, output);

    public async Task CancelAsync(string correlationId, CancellationToken ct = default)
    {
        var name = SafeContainerName(correlationId);
        // Kill the container; --rm will clean it up on stop, but kill + rm handles the edge case
        // where the container is stuck and won't auto-remove.
        try { await RunDockerAsync(ct, "kill", name); } catch { /* best-effort */ }
        try { await RunDockerAsync(ct, "rm", "--force", name); } catch { /* best-effort */ }
    }

    public async Task<WorkloadRunResult> RunAsync(WorkloadRunRequest request, CancellationToken ct = default)
    {
        // ── Resolve image and optional code-work-dir ──────────────────────────────────────────────
        string image;
        string? workDir = null;          // host-side temp dir for code source
        string[]? overrideCmd = null;    // docker CMD override for code workloads
        var depsBaked = false;           // true when running from a warm pcwarm-* image

        if (request.CodeFiles is not null)
        {
            // CodeWorkload: look up the runtime registry.
            var runtimeId = request.RuntimeId
                ?? throw new InvalidOperationException("RuntimeId is required for code workloads.");

            if (!_options.Runtimes.TryGetValue(runtimeId, out var runtime))
                throw new InvalidOperationException(
                    $"Unknown runtimeId '{runtimeId}'. Configured runtimes: {string.Join(", ", _options.Runtimes.Keys)}.");

            if (string.IsNullOrWhiteSpace(runtime.BaseImage))
                throw new InvalidOperationException($"Runtime '{runtimeId}' has no BaseImage configured.");

            image = runtime.BaseImage;

            var entrypoint = !string.IsNullOrWhiteSpace(request.Entrypoint)
                ? request.Entrypoint
                : runtime.DefaultEntrypoint;

            if (string.IsNullOrWhiteSpace(entrypoint))
                throw new InvalidOperationException($"Runtime '{runtimeId}' has no DefaultEntrypoint and no entrypoint was provided.");

            // Materialise the source file set to a temp work dir — mounted read-only at /work.
            workDir = Path.Combine(Path.GetTempPath(), $"pcwk-{request.CorrelationId}");
            Directory.CreateDirectory(workDir);

            if (string.IsNullOrWhiteSpace(request.Entrypoint) && request.CodeFiles.Count == 1)
            {
                // Single-file workload with no explicit entrypoint: write it at the runtime default name.
                await WriteWorkFileAsync(workDir, entrypoint!, request.CodeFiles[0].Content, ct);
            }
            else
            {
                // Multi-file workload: write each file at its own path (subdirectories created as needed).
                foreach (var (path, content) in request.CodeFiles)
                    await WriteWorkFileAsync(workDir, path, content, ct);
            }

            // Build CMD override: replace {entrypoint} token in each command segment.
            overrideCmd = runtime.InvokeCommand
                .Select(seg => seg.Replace("{entrypoint}", entrypoint, StringComparison.Ordinal))
                .ToArray();

            // A job shipping its runtime's dependency manifest (requirements.txt, package.json,
            // Gemfile, go.mod) gets its packages installed first — but not on every run: the layer
            // is baked once into a pcwarm-* image and reused from then on (EnsureWarmImageAsync).
            // Only the cold fallback (warming disabled or the build failed) still pays the per-run
            // install — whose downloads then need the job's AllowNetworkEgress, as before.
            if (WorkloadDependencies.For(runtimeId, request.CodeFiles) is { } recipe)
            {
                var warm = _options.WarmImages
                    ? await EnsureWarmImageAsync(runtimeId, image, recipe, request.CodeFiles, ct)
                    : null;
                if (warm is not null)
                {
                    image = warm;
                    depsBaked = true;
                    if (recipe.InvokePrefix is not null)
                        overrideCmd = recipe.InvokePrefix
                            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Concat(overrideCmd).ToArray();
                }
                else
                {
                    overrideCmd = WorkloadDependencies.WrapDockerCommand(recipe, overrideCmd);
                }
            }
        }
        else
        {
            image = request.Image
                ?? throw new InvalidOperationException("Either Image or CodeSource must be provided.");
        }

        var hostOutDir = Path.Combine(Path.GetTempPath(), $"pcw-{request.CorrelationId}");
        Directory.CreateDirectory(hostOutDir);
        // The container runs as RunAsUser (nobody) — a uid that owns nothing on the host — so the
        // fresh bind-mounted /out (700/755 owned by this process) would refuse its artifact writes.
        MakeWorldWritable(hostOutDir);

        // ── Materialise ArtifactMounts ────────────────────────────────────────────────────────────
        var mountDir = Path.Combine(Path.GetTempPath(), $"pcwm-{request.CorrelationId}");
        var hostMounts = new List<(string HostPath, string ContainerPath)>();
        if (request.ArtifactMounts.Count > 0)
        {
            Directory.CreateDirectory(mountDir);
            foreach (var (content, containerPath) in request.ArtifactMounts)
            {
                var safeName = containerPath.Replace('/', '_').TrimStart('_');
                var hostFile = Path.Combine(mountDir, safeName);
                var dir = Path.GetDirectoryName(hostFile)!;
                Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(hostFile, content, ct);
                hostMounts.Add((hostFile, containerPath));
            }
        }

        // Per-job timeout wins; fall back to the global default when the job didn't set one.
        var timeoutSeconds = request.TimeoutSeconds is > 0 ? request.TimeoutSeconds.Value : _options.DefaultTimeoutSeconds;
        try
        {
            var args = BuildArgs(request, image, hostOutDir, hostMounts, workDir, overrideCmd, depsBaked);
            var psi = BuildProcessStartInfo(args);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start '{_options.DockerExecutable}'.");

            await proc.StandardInput.WriteAsync(request.StdinPayload);
            proc.StandardInput.Close();

            var stdoutTask = CaptureStreamAsync(proc.StandardOutput, request.CorrelationId, isError: false, timeoutCts.Token);
            var stderrTask = CaptureStreamAsync(proc.StandardError, request.CorrelationId, isError: true, timeoutCts.Token);
            await proc.WaitForExitAsync(timeoutCts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var exitCode = proc.ExitCode;

            var artifactPath = Path.Combine(hostOutDir, _options.ArtifactFileName);
            string? artifact = null;
            if (File.Exists(artifactPath))
                artifact = await File.ReadAllTextAsync(artifactPath, ct);

            var artifacts = await CaptureNamedArtifactsAsync(hostOutDir, ct);

            return new WorkloadRunResult(exitCode, artifact, stdout ?? "", stderr ?? "", artifacts);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new WorkloadRunResult(
                ExitCode: -1,
                Artifact: null,
                Stdout: "",
                Stderr: $"Workload timed out after {timeoutSeconds}s (correlationId={request.CorrelationId}).");
        }
        finally
        {
            _output?.Complete(request.CorrelationId);
            try { Directory.Delete(hostOutDir, recursive: true); } catch { /* best-effort */ }
            if (request.ArtifactMounts.Count > 0)
                try { Directory.Delete(mountDir, recursive: true); } catch { /* best-effort */ }
            if (workDir is not null)
                try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private async Task<string> CaptureStreamAsync(
        StreamReader reader, string correlationId, bool isError, CancellationToken ct)
    {
        var captured = new StringBuilder();
        var buffer = new char[2048];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0) break;
            var chunk = new string(buffer, 0, read);
            captured.Append(chunk);
            _output?.Append(correlationId, chunk, isError);
        }
        return captured.ToString();
    }

    /// <summary>
    /// Resolves the <c>pcwarm-*</c> image for this dependency layer, building it on first use:
    /// base image + manifest COPYs + the recipe's bake install + the ENV that lets the runtime
    /// resolve the baked deps. The gate serializes concurrent builders of the SAME layer; a build
    /// that fails for any reason returns null and the caller runs the cold per-run install.
    /// </summary>
    private async Task<string?> EnsureWarmImageAsync(string runtimeId, string baseImage,
        WorkloadDependencyRecipe recipe, IReadOnlyList<(string Path, string Content)> codeFiles, CancellationToken ct)
    {
        var tag = $"pcwarm-{SanitizeRepo(runtimeId)}:{WorkloadDependencies.BakeKey(runtimeId, baseImage, recipe, codeFiles)}";
        var gate = _buildGates.GetOrAdd(tag, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (await RunDockerAsync(ct, "image", "inspect", tag) is { ExitCode: 0 })
                return tag;

            var contextDir = Path.Combine(Path.GetTempPath(), $"pcbld-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(contextDir);
                await File.WriteAllTextAsync(Path.Combine(contextDir, "Dockerfile"),
                    WorkloadDependencies.Dockerfile(baseImage, recipe, codeFiles), ct);
                foreach (var (path, content) in WorkloadDependencies.ManifestFiles(recipe, codeFiles))
                    await File.WriteAllTextAsync(Path.Combine(contextDir, path), content, ct);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(WarmBuildTimeout);
                var build = await RunDockerAsync(timeoutCts.Token, "build", "-t", tag, contextDir);
                if (build.ExitCode == 0)
                {
                    _log?.LogInformation("Built warm dependency image {Tag} for runtime '{Runtime}'.", tag, runtimeId);
                    return tag;
                }
                _log?.LogWarning("Warm image build for {Tag} failed (exit {ExitCode}); falling back to the per-run install.\n{Output}",
                    tag, build.ExitCode, Truncate(build.Output));
            }
            finally
            {
                try { Directory.Delete(contextDir, recursive: true); } catch { /* best-effort */ }
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Warm image build for {Tag} failed; falling back to the per-run install.", tag);
        }
        finally
        {
            gate.Release();
        }
        return null;
    }

    /// <summary>Runs the docker CLI and returns its exit code + combined output (build/inspect plumbing).</summary>
    private async Task<(int ExitCode, string Output)> RunDockerAsync(CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _options.DockerExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{_options.DockerExecutable}'.");
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw;
        }
        return (proc.ExitCode, await stdout + await stderr);
    }

    /// <summary>Produces a safe Docker container name from a correlation id.
    /// Docker container names: [a-zA-Z0-9][a-zA-Z0-9_.-]+, max 128 chars.</summary>
    internal static string SafeContainerName(string correlationId)
    {
        const int maxLen = 120;
        var sb = new StringBuilder("pcw-");
        foreach (var c in correlationId)
        {
            if (sb.Length >= maxLen) break;
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
                sb.Append(c);
            else if (c is ':')
                sb.Append('-');
        }
        return sb.ToString().TrimEnd('-', '_', '.');
    }

    // Docker repository names: lowercase letters, digits, and - _ . only.
    private static string SanitizeRepo(string runtimeId)
    {
        var sb = new StringBuilder(runtimeId.Length);
        foreach (var c in runtimeId.ToLowerInvariant())
            sb.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.' ? c : '-');
        return sb.ToString();
    }

    private static string Truncate(string s) => s.Length <= 4000 ? s : s[..4000] + "…";

    /// <summary>
    /// Captures every file written to <paramref name="hostOutDir"/> except the primary result.json, as named
    /// artifacts (e.g. report.csv, listings.pdf). Names are relative paths within /out; binary files ride as
    /// base64 (see <see cref="WorkloadArtifact.FromBytes"/>). Bounded for safety: skips files larger than
    /// 5 MB and caps the set at 50 files.
    /// </summary>
    private async Task<List<WorkloadArtifact>> CaptureNamedArtifactsAsync(string hostOutDir, CancellationToken ct)
    {
        const long maxBytes = 5L * 1024 * 1024;
        const int maxFiles = 50;
        var artifacts = new List<WorkloadArtifact>();
        if (!Directory.Exists(hostOutDir)) return artifacts;

        foreach (var path in Directory.EnumerateFiles(hostOutDir, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(hostOutDir, path).Replace(Path.DirectorySeparatorChar, '/');
            if (string.Equals(name, _options.ArtifactFileName, StringComparison.Ordinal))
                continue; // the primary result.json is surfaced as Artifact

            try
            {
                if (new FileInfo(path).Length > maxBytes) continue;
                artifacts.Add(WorkloadArtifact.FromBytes(name, await File.ReadAllBytesAsync(path, ct)));
                if (artifacts.Count >= maxFiles) break;
            }
            catch { /* best-effort: skip unreadable files */ }
        }

        return artifacts;
    }

    /// <summary>
    /// Writes one source file under <paramref name="workDir"/> at <paramref name="relativePath"/>, creating
    /// subdirectories as needed. Defends against path traversal: the resolved path must stay within workDir.
    /// </summary>
    private static async Task WriteWorkFileAsync(string workDir, string relativePath, string content, CancellationToken ct)
    {
        var rooted = Path.GetFullPath(Path.Combine(workDir, relativePath));
        var workRoot = Path.GetFullPath(workDir) + Path.DirectorySeparatorChar;
        if (!rooted.StartsWith(workRoot, StringComparison.Ordinal))
            throw new InvalidOperationException($"Refusing to write file outside the work dir: '{relativePath}'.");

        Directory.CreateDirectory(Path.GetDirectoryName(rooted)!);
        await File.WriteAllTextAsync(rooted, content, ct);
    }

    /// <summary>
    /// Opens a fresh host temp dir to any uid (rwxrwxrwx): job containers run as
    /// <see cref="WorkloadRunnerOptions.RunAsUser"/> (nobody), which owns nothing on the host and
    /// otherwise cannot write into a 700/755 dir owned by this process. No-op off Unix.
    /// </summary>
    private static void MakeWorldWritable(string dir)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
    }

    /// <summary>
    /// Builds the docker argv array. User-supplied values are always added as separate elements in
    /// the ArgumentList (never string-interpolated into a shell command) — avoiding injection.
    /// Sandbox defaults are applied here from options. Internal: the argv is the runner's contract,
    /// so tests assert against it directly.
    /// </summary>
    internal List<string> BuildArgs(
        WorkloadRunRequest request,
        string image,
        string hostOutDir,
        List<(string HostPath, string ContainerPath)> hostMounts,
        string? workDir,
        string[]? overrideCmd,
        bool depsBaked)
    {
        var args = new List<string> { "run", "--rm", "-i" };

        // Named so CancelAsync can find and kill it.
        args.Add("--name");
        args.Add(SafeContainerName(request.CorrelationId));

        // ── Sandbox: resource limits ──────────────────────────────────────────────────────────────
        if (_options.SandboxPidsLimit > 0)
        {
            args.Add("--pids-limit");
            args.Add(_options.SandboxPidsLimit.ToString());
        }

        if (!string.IsNullOrWhiteSpace(_options.SandboxMemory))
        {
            args.Add("--memory");
            args.Add(_options.SandboxMemory);
        }

        if (_options.SandboxCpus > 0)
        {
            args.Add("--cpus");
            args.Add(_options.SandboxCpus.ToString("G"));
        }

        // ── Sandbox: read-only root fs with writable /out and /tmp ────────────────────────────────
        if (_options.SandboxReadOnly)
        {
            args.Add("--read-only");
            args.Add("--tmpfs");
            args.Add("/tmp:rw,noexec,nosuid,size=64m");
        }

        // ── Dependency installs: writable, exec-capable scratch ──────────────────────────────────
        // The /tmp tmpfs above is deliberately noexec; dependency installs (node_modules native
        // addons, pip wheels with .so files) need their own exec mount. Only when a recipe applies
        // AND the run is cold — a warm pcwarm-* image has the layer baked in, no scratch needed.
        if (!depsBaked && WorkloadDependencies.For(request.RuntimeId, request.CodeFiles) is not null)
        {
            args.Add("--tmpfs");
            args.Add($"{WorkloadDependencies.DockerDepsRoot}:rw,exec,nosuid,size=512m");
        }

        // ── Sandbox: network isolation ────────────────────────────────────────────────────────────
        // Per-job AllowNetworkEgress wins over the global SandboxNetworkNone default.
        // Apply --network none only when the global option is set AND the job has not opted in.
        if (_options.SandboxNetworkNone && !request.AllowNetworkEgress)
        {
            args.Add("--network");
            args.Add("none");
        }

        // ── Sandbox: never run as root ────────────────────────────────────────────────────────────
        // A numeric uid/gid needs no /etc/passwd entry in the image. cap-drop ALL + no-new-privileges
        // keep the job unprivileged even if a setuid binary or image misconfig slips past the uid.
        args.Add("--user");
        args.Add($"{_options.RunAsUser}:{_options.RunAsGroup}");
        args.Add("--cap-drop");
        args.Add("ALL");
        args.Add("--security-opt");
        args.Add("no-new-privileges");

        // ── Artifact output dir ───────────────────────────────────────────────────────────────────
        args.Add("-v");
        args.Add($"{hostOutDir}:/out");

        // ── Code work dir (CodeWorkload only) ─────────────────────────────────────────────────────
        if (workDir is not null)
        {
            args.Add("-v");
            args.Add($"{workDir}:/work:ro");
        }

        // Warm images run their plain argv (no sh -c wrap), so nothing cd's to /work — anchor the
        // working directory for module-context-sensitive runtimes (go run, bundle exec).
        if (depsBaked)
        {
            args.Add("-w");
            args.Add("/work");
        }

        // ── Reduce artifact mounts ────────────────────────────────────────────────────────────────
        foreach (var (hostPath, containerPath) in hostMounts)
        {
            args.Add("-v");
            args.Add($"{hostPath}:{containerPath}:ro");
        }

        // ── Environment variables ─────────────────────────────────────────────────────────────────
        // A writable HOME for tools that insist on one (npm cache, go env) — /tmp is the
        // world-writable tmpfs. Added before the job's own env so the job can still override it.
        args.Add("-e");
        args.Add("HOME=/tmp");
        foreach (var (key, value) in request.Env)
        {
            args.Add("-e");
            args.Add($"{key}={value}");
        }

        // ── Image ─────────────────────────────────────────────────────────────────────────────────
        args.Add(image);

        // ── CMD override (CodeWorkload: runtime invoke command) ───────────────────────────────────
        if (overrideCmd is not null)
            args.AddRange(overrideCmd);

        return args;
    }

    private ProcessStartInfo BuildProcessStartInfo(List<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _options.DockerExecutable,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        return psi;
    }
}
