namespace PlaceContext.Infrastructure.Workload;

/// <summary>
/// Options for <see cref="DockerWorkloadRunner"/>. Bound from the
/// "PlaceContext:WorkloadRunner" configuration section.
/// </summary>
public sealed class WorkloadRunnerOptions
{
    /// <summary>Kubernetes namespace used for untrusted workload Jobs.</summary>
    public string KubernetesNamespace { get; set; } = "placecontext-jobs";

    /// <summary>
    /// Docker CLI executable name or path. Default: "docker".
    /// Override to "podman" or a fully-qualified path in restricted environments.
    /// </summary>
    public string DockerExecutable { get; set; } = "docker";

    /// <summary>
    /// Filename of the artifact written by each container to /out. Default: "result.json".
    /// Configurable without code changes — PlaceContext does not interpret the content.
    /// </summary>
    public string ArtifactFileName { get; set; } = "result.json";

    /// <summary>Per-container timeout in seconds. Default: 1800 (30 minutes).</summary>
    public int DefaultTimeoutSeconds { get; set; } = 1800;

    // ── Sandbox defaults ─────────────────────────────────────────────────────────────────────────
    // Applied to every container run. Overridable via configuration. All off-by-default options
    // are documented so operators can tune them.

    /// <summary>
    /// Maximum number of processes a container may spawn (pids-limit). 0 = unlimited (not recommended).
    /// Default: 64.
    /// </summary>
    public int SandboxPidsLimit { get; set; } = 64;

    /// <summary>
    /// Memory limit passed to docker (e.g. "256m", "512m"). Empty string = no limit.
    /// Default: "256m".
    /// </summary>
    public string SandboxMemory { get; set; } = "256m";

    /// <summary>
    /// CPU quota fraction (e.g. 0.5 = half a CPU). 0 = no limit.
    /// Default: 1.0 (one full CPU).
    /// </summary>
    public double SandboxCpus { get; set; } = 1.0;

    /// <summary>
    /// Mount the container's root filesystem read-only (--read-only). A tmpfs at /tmp is added
    /// automatically when true so the container still has writable scratch space.
    /// Default: true.
    /// </summary>
    public bool SandboxReadOnly { get; set; } = true;

    /// <summary>
    /// Disable network access for containers by default (--network none).
    /// Per-job egress can be enabled via <see cref="PlaceContext.Application.Ports.WorkloadRunRequest"/>
    /// when the caller explicitly opts in.
    /// Default: true.
    /// </summary>
    public bool SandboxNetworkNone { get; set; } = true;

    /// <summary>
    /// UID job containers run as (docker --user, k8s securityContext.runAsUser). Jobs never run as
    /// root: the default 65534 is "nobody". A numeric UID works on every image — no /etc/passwd
    /// entry is required. long, matching the Kubernetes API; config binding accepts either width.
    /// Default: 65534.
    /// </summary>
    public long RunAsUser { get; set; } = 65534;

    /// <summary>
    /// GID job containers run as (docker --user, k8s securityContext.runAsGroup / fsGroup).
    /// Default: 65534 ("nogroup").
    /// </summary>
    public long RunAsGroup { get; set; } = 65534;

    /// <summary>
    /// Bake dependency layers into reusable warm images: a code workload shipping its runtime's
    /// manifest (requirements.txt, package.json, Gemfile, go.mod) gets a
    /// <c>pcwarm-&lt;runtime&gt;:&lt;hash&gt;</c> image built once (base image + package install) and
    /// every later run of the same manifest starts from it instead of reinstalling per container.
    /// Builds download packages on the HOST network (job code itself still runs --network none
    /// unless it opts into egress). Any build failure falls back to the per-run install.
    /// Default: true.
    /// </summary>
    public bool WarmImages { get; set; } = true;

    // ── Job placement (Kubernetes runner only) ──────────────────────────────────────────────────
    // Where job pods land in a multi-node fleet. The common shape is a small cloud server (e.g. a
    // DigitalOcean droplet) running the portal/MCP as the k3s control plane, with the operator's
    // own machines joined as agents over Tailscale — jobs should execute on those local machines,
    // not burn the portal server's CPU.

    /// <summary>
    /// Prefer scheduling job pods onto worker (agent) nodes — any node NOT carrying the
    /// <c>node-role.kubernetes.io/control-plane</c> label. Soft preference: on a single-node
    /// install jobs still run on the server. Default: true.
    /// </summary>
    public bool PreferWorkerNodes { get; set; } = true;

    /// <summary>
    /// Require worker (agent) nodes for job pods. When true a job pod is never scheduled onto the
    /// control-plane node — it stays Pending until a worker is online. Turn this on when the
    /// control plane is a cloud portal server and all execution must happen on local machines.
    /// Default: false.
    /// </summary>
    public bool RequireWorkerNodes { get; set; }

    /// <summary>
    /// Explicit node selector for job pods (label → value), e.g.
    /// <c>{"placecontext.io/jobs": "true"}</c> to pin execution to hand-picked machines.
    /// Applied in addition to the worker-node affinity above. Empty = any node.
    /// </summary>
    public Dictionary<string, string> JobNodeSelector { get; set; } = new();

    /// <summary>
    /// Bake the dependency layer once per manifest hash and reuse it from the object store on
    /// later runs (Kubernetes runner only): a bake Job installs the packages, tars them and PUTs
    /// them to S3; warmed shard pods fetch + extract the tar in their init step and skip the install.
    /// Requires a configured object store and a job that opted into public network egress; silently
    /// off otherwise. Any failure falls back to the per-run install. Default: true.
    /// </summary>
    public bool WarmDependencyCache { get; set; } = true;

    // ── Runtime registry ────────────────────────────────────────────────────────────────────────
    // Maps runtimeId → { BaseImage, InvokeCommand, DefaultEntrypoint }.
    // Config-driven — no domain-specific knowledge, just generic runtime sandbox definitions.

    /// <summary>
    /// Mapping from runtimeId (e.g. "node", "python") to its sandbox definition.
    /// Ships with built-in defaults for "node" and "python"; extend via configuration.
    /// </summary>
    public Dictionary<string, RuntimeDefinition> Runtimes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["node"] = new RuntimeDefinition
        {
            BaseImage = "node:22-slim",
            InvokeCommand = new[] { "node", "/work/{entrypoint}" },
            DefaultEntrypoint = "index.js",
        },
        ["python"] = new RuntimeDefinition
        {
            BaseImage = "python:3.12-slim",
            InvokeCommand = new[] { "python", "/work/{entrypoint}" },
            DefaultEntrypoint = "main.py",
        },
        ["go"] = new RuntimeDefinition
        {
            BaseImage = "golang:1.23-alpine",
            InvokeCommand = new[] { "go", "run", "/work/{entrypoint}" },
            DefaultEntrypoint = "main.go",
            // `go run` compiles into GOCACHE and execs the binary from there — the sandbox /tmp
            // tmpfs is deliberately noexec, so without a module (no recipe) every run failed with
            // "permission denied". The profile moves HOME/GOCACHE/GOTMPDIR onto an exec tmpfs.
            SandboxMemory = "512m",
            ExtraTmpfs = new[] { WorkloadDependencies.DockerDepsRoot + ":rw,exec,nosuid,size=256m" },
            Env = new()
            {
                ["HOME"] = "{deps}/home",
                ["GOCACHE"] = "{deps}/gocache",
                ["GOTMPDIR"] = "{deps}/gotmp",
            },
            PreInvokeScript = "mkdir -p \"$HOME\" \"$GOCACHE\" \"$GOTMPDIR\"",
        },
        ["ruby"] = new RuntimeDefinition
        {
            BaseImage = "ruby:3.3-slim",
            InvokeCommand = new[] { "ruby", "/work/{entrypoint}" },
            DefaultEntrypoint = "main.rb",
        },
        // .NET 10 file-based apps: `dotnet run app.cs` runs a single C# file with no project file.
        // The first run implicitly restores ~300MB of runtime packs into $NUGET_PACKAGES and execs
        // the built binary from a cache — impossible under the default sandbox (64MB noexec /tmp,
        // 256m memory). The profile gives the toolchain a writable+exec scratch root and room to
        // build; the always-on dotnet recipe (WorkloadDependencies) bakes the restored packs into a
        // warm image once so sealed (no-network) runs restore offline from the baked fallback.
        ["dotnet"] = new RuntimeDefinition
        {
            BaseImage = "mcr.microsoft.com/dotnet/sdk:10.0",
            InvokeCommand = new[] { "dotnet", "run", "/work/{entrypoint}" },
            DefaultEntrypoint = "main.cs",
            SandboxMemory = "1g",
            ExtraTmpfs = new[] { WorkloadDependencies.DockerDepsRoot + ":rw,exec,nosuid,size=512m" },
            Env = new()
            {
                ["HOME"] = "{deps}/home",
                ["XDG_DATA_HOME"] = "{deps}/xdg",          // the runfile build cache execs from here
                ["NUGET_PACKAGES"] = "{deps}/nuget",
                ["DOTNET_CLI_HOME"] = "{deps}/dotnet",
                ["TMPDIR"] = "{deps}/tmp",
            },
            // The SDK requires these dirs to actually exist (notably for #:property directives).
            PreInvokeScript = "mkdir -p \"$HOME\" \"$XDG_DATA_HOME\" \"$NUGET_PACKAGES\" \"$DOTNET_CLI_HOME\" \"$TMPDIR\"",
        },
    };
}
