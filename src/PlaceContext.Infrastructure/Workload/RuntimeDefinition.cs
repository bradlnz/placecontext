namespace PlaceContext.Infrastructure.Workload;

/// <summary>
/// Defines how a generic runtime sandbox is launched.
/// The <see cref="InvokeCommand"/> array is used as the docker CMD override;
/// the token <c>{entrypoint}</c> is replaced with the effective entry-point filename.
/// </summary>
public sealed class RuntimeDefinition
{
    /// <summary>Base container image (e.g. "node:22-slim", "python:3.12-slim").</summary>
    public string BaseImage { get; set; } = "";

    /// <summary>
    /// Command array template (docker CMD override). Use <c>{entrypoint}</c> as a placeholder.
    /// Example: ["node", "/work/{entrypoint}"]
    /// </summary>
    public string[] InvokeCommand { get; set; } = Array.Empty<string>();

    /// <summary>Default entry-point filename when the job doesn't specify one (e.g. "index.js").</summary>
    public string DefaultEntrypoint { get; set; } = "";

    // ── Always-on sandbox profile ────────────────────────────────────────────────────────────────
    // Applied to EVERY run of this runtime, manifest or not — for toolchains that cannot execute
    // under the baseline sandbox at all (dotnet's implicit restore + runfile cache, go's compile
    // cache: both must write and exec outside the noexec /tmp tmpfs). All fields optional; runtimes
    // without them behave exactly as before.

    /// <summary>
    /// Overrides <see cref="WorkloadRunnerOptions.SandboxMemory"/> for this runtime (e.g. "1g").
    /// Applied by both runners (docker --memory, k8s memory limit). Null = the global default.
    /// </summary>
    public string? SandboxMemory { get; set; }

    /// <summary>
    /// Extra tmpfs mounts (docker --tmpfs syntax, e.g. "/pcdeps:rw,exec,nosuid,size=512m"), mounted
    /// on every run of this runtime — including warm runs, where the dependency scratch mount is
    /// otherwise skipped. Docker runner only; on Kubernetes /work is already a writable emptyDir.
    /// </summary>
    public string[] ExtraTmpfs { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Environment variables the toolchain needs (e.g. HOME, GOCACHE). The <c>{deps}</c> token
    /// resolves to the runner's writable deps root (/pcdeps on Docker, /work/.pcdeps on Kubernetes).
    /// Applied after the default HOME=/tmp and before the job's own env, so jobs can still override.
    /// </summary>
    public Dictionary<string, string> Env { get; set; } = new();

    /// <summary>
    /// Shell snippet run inside the container before the invoke command (command becomes
    /// <c>sh -c '&lt;script&gt; &amp;&amp; exec &lt;cmd&gt;'</c>), e.g. mkdir -p the env dirs the
    /// toolchain requires to exist. Compose-safe: wraps an already recipe-wrapped command.
    /// </summary>
    public string? PreInvokeScript { get; set; }
}
