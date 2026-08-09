namespace PlaceContext.Application.Ports;

/// <summary>
/// Input to a single container invocation.
/// All content is opaque: PlaceContext does not parse <see cref="StdinPayload"/> or validate env keys.
/// Exactly one of Image (ImageWorkload) or CodeSource+RuntimeId (CodeWorkload) should be set.
/// </summary>
/// <param name="Image">
///   Container image to run (ImageWorkload path). Null when CodeSource is set.
/// </param>
/// <param name="StdinPayload">Opaque string written to the container's STDIN.</param>
/// <param name="Env">Key/value pairs injected as environment variables.</param>
/// <param name="ArtifactMounts">
///   Optional artifact content to make available to the container as read-only files.
///   Each entry is (content, containerPath). The runner writes the content to a temp file
///   and mounts it read-only at containerPath. Used for the reduce step to pass shard artifacts.
/// </param>
/// <param name="CorrelationId">Stable identifier for this invocation (used in temp dir naming and logs).</param>
/// <param name="CodeFiles">
///   Inline source files to materialise into the runtime work dir (CodeWorkload path).
///   Null when Image is set. The runner writes each file under /work at its path (creating
///   subdirectories), then invokes the runtime against the resolved <paramref name="Entrypoint"/>.
/// </param>
/// <param name="RuntimeId">
///   Runtime registry key (e.g. "node", "python"). Used to look up the base image and invoke
///   command from WorkloadRunnerOptions. Null when Image is set.
/// </param>
/// <param name="Entrypoint">
///   Entry-point path within the work dir (e.g. "index.js", "lib/main.py"). When null the runtime's
///   default entrypoint from the registry is used and the single supplied file is written there.
/// </param>
/// <param name="AllowNetworkEgress">
///   When true, Docker's default bridge network is used (outbound network permitted).
///   When false (default), <c>--network none</c> is applied — no network access.
///   The per-job flag always wins over the global <c>WorkloadRunnerOptions.SandboxNetworkNone</c>.
/// </param>
public sealed record WorkloadRunRequest(
    string? Image,
    string StdinPayload,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<(string Content, string ContainerPath)> ArtifactMounts,
    string CorrelationId,
    IReadOnlyList<(string Path, string Content)>? CodeFiles,
    string? RuntimeId,
    string? Entrypoint,
    bool AllowNetworkEgress = true,
    int? TimeoutSeconds = null);
