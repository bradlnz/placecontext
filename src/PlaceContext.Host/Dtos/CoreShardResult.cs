namespace PlaceContext.Host.Api;

public sealed record CoreShardResult(
    int Index,
    int ExitCode,
    string Outcome,
    string? Artifact,
    string? Log,
    IReadOnlyList<CoreRunArtifact> Artifacts);
