namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobRunShardPageResponse(
    int Index,
    int ExitCode,
    string Outcome,
    string? Artifact,
    string? Log);
