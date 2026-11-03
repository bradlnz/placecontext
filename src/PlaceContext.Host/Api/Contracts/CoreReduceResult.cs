namespace PlaceContext.Host.Api;

public sealed record CoreReduceResult(
    int ExitCode,
    bool Succeeded,
    string? Artifact,
    string? Log,
    IReadOnlyList<CoreRunArtifact> Artifacts);
