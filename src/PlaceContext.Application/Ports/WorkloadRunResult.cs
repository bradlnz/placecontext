namespace PlaceContext.Application.Ports;

/// <summary>
/// Raw result from one container invocation.
/// </summary>
/// <param name="ExitCode">The container's exit code.</param>
/// <param name="Artifact">
///   Opaque text content of the artifact file (e.g. result.json). Null if the file was not written.
/// </param>
/// <param name="Stdout">Standard output captured from the container. May be truncated.</param>
/// <param name="Stderr">Standard error captured from the container. May be truncated.</param>
/// <param name="Artifacts">
///   Named output files captured from /out beyond the primary <see cref="Artifact"/> (e.g. report.csv).
///   Each is (name, textContent). Empty when the step produced only result.json (or nothing).
/// </param>
public sealed record WorkloadRunResult(
    int ExitCode,
    string? Artifact,
    string Stdout,
    string Stderr,
    IReadOnlyList<WorkloadArtifact>? Artifacts = null);
