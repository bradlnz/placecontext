using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Workload;

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
/// • For reduce containers: mounts each supplied ArtifactMount (content → containerPath) read-only.
/// • Sandbox defaults applied to every run (configurable): --pids-limit, --memory, --cpus,
///   --read-only + /tmp tmpfs, --network none by default. Never string-interpolates user values
///   into a shell — docker argv is built as an array.
/// • Captures exit code, stdout, and stderr. Cleans up temp dirs on completion.
/// • Timeout handled: returns synthetic exit code -1 result (not exception) on timeout.
/// </summary>
public sealed class DockerWorkloadRunner : IWorkloadRunner
{
    private readonly WorkloadRunnerOptions _options;

    public DockerWorkloadRunner(IOptions<WorkloadRunnerOptions> options)
        => _options = options.Value;

    public async Task<WorkloadRunResult> RunAsync(WorkloadRunRequest request, CancellationToken ct = default)
    {
        // ── Resolve image and optional code-work-dir ──────────────────────────────────────────────
        string image;
        string? workDir = null;          // host-side temp dir for code source
        string[]? overrideCmd = null;    // docker CMD override for code workloads

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
        }
        else
        {
            image = request.Image
                ?? throw new InvalidOperationException("Either Image or CodeSource must be provided.");
        }

        var hostOutDir = Path.Combine(Path.GetTempPath(), $"pcw-{request.CorrelationId}");
        Directory.CreateDirectory(hostOutDir);

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
            var args = BuildArgs(request, image, hostOutDir, hostMounts, workDir, overrideCmd);
            var psi = BuildProcessStartInfo(args);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start '{_options.DockerExecutable}'.");

            await proc.StandardInput.WriteAsync(request.StdinPayload);
            proc.StandardInput.Close();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(timeoutCts.Token);
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
            try { Directory.Delete(hostOutDir, recursive: true); } catch { /* best-effort */ }
            if (request.ArtifactMounts.Count > 0)
                try { Directory.Delete(mountDir, recursive: true); } catch { /* best-effort */ }
            if (workDir is not null)
                try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Captures every file written to <paramref name="hostOutDir"/> except the primary result.json, as named
    /// text artifacts (e.g. report.csv). Names are relative paths within /out. Bounded for safety: skips
    /// files larger than 5 MB and caps the set at 50 files.
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
                artifacts.Add(new WorkloadArtifact(name, await File.ReadAllTextAsync(path, ct)));
                if (artifacts.Count >= maxFiles) break;
            }
            catch { /* best-effort: skip unreadable/binary files */ }
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
    /// Builds the docker argv array. User-supplied values are always added as separate elements in
    /// the ArgumentList (never string-interpolated into a shell command) — avoiding injection.
    /// Sandbox defaults are applied here from options.
    /// </summary>
    private List<string> BuildArgs(
        WorkloadRunRequest request,
        string image,
        string hostOutDir,
        List<(string HostPath, string ContainerPath)> hostMounts,
        string? workDir,
        string[]? overrideCmd)
    {
        var args = new List<string> { "run", "--rm", "-i" };

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

        // ── Sandbox: network isolation ────────────────────────────────────────────────────────────
        // Per-job AllowNetworkEgress wins over the global SandboxNetworkNone default.
        // Apply --network none only when the global option is set AND the job has not opted in.
        if (_options.SandboxNetworkNone && !request.AllowNetworkEgress)
        {
            args.Add("--network");
            args.Add("none");
        }

        // ── Artifact output dir ───────────────────────────────────────────────────────────────────
        args.Add("-v");
        args.Add($"{hostOutDir}:/out");

        // ── Code work dir (CodeWorkload only) ─────────────────────────────────────────────────────
        if (workDir is not null)
        {
            args.Add("-v");
            args.Add($"{workDir}:/work:ro");
        }

        // ── Reduce artifact mounts ────────────────────────────────────────────────────────────────
        foreach (var (hostPath, containerPath) in hostMounts)
        {
            args.Add("-v");
            args.Add($"{hostPath}:{containerPath}:ro");
        }

        // ── Environment variables ─────────────────────────────────────────────────────────────────
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
