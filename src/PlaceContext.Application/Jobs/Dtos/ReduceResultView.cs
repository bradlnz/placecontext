namespace PlaceContext.Application.Dtos;

/// <summary>Read model for the reduce result.</summary>
public sealed record ReduceResultView(
    int ExitCode,
    bool Succeeded,
    string? Artifact,
    string? Log,
    IReadOnlyList<RunArtifactView> Artifacts);
