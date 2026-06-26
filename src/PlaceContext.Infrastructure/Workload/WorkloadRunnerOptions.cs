namespace PlaceContext.Infrastructure.Workload;

/// <summary>
/// Options for <see cref="DockerWorkloadRunner"/>. Bound from the
/// "PlaceContext:WorkloadRunner" configuration section.
/// </summary>
public sealed class WorkloadRunnerOptions
{
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

    /// <summary>Per-container timeout in seconds. Default: 300 (5 minutes).</summary>
    public int DefaultTimeoutSeconds { get; set; } = 300;

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
        },
        ["ruby"] = new RuntimeDefinition
        {
            BaseImage = "ruby:3.3-slim",
            InvokeCommand = new[] { "ruby", "/work/{entrypoint}" },
            DefaultEntrypoint = "main.rb",
        },
        // .NET 10 file-based apps: `dotnet run app.cs` runs a single C# file with no project file.
        ["dotnet"] = new RuntimeDefinition
        {
            BaseImage = "mcr.microsoft.com/dotnet/sdk:10.0",
            InvokeCommand = new[] { "dotnet", "run", "/work/{entrypoint}" },
            DefaultEntrypoint = "main.cs",
        },
    };
}

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
}
