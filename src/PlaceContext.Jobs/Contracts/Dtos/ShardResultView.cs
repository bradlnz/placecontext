namespace PlaceContext.Application.Dtos;

/// <summary>Read model for a single shard result.</summary>
public sealed record ShardResultView(
    int Index,
    int ExitCode,
    string Outcome,
    string? Artifact,
    string? Log,
    IReadOnlyList<RunArtifactView> Artifacts);
