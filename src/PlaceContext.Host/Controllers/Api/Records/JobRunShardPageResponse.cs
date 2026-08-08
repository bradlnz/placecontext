namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record JobRunShardPageResponse(
    int Index,
    int ExitCode,
    string Outcome,
    string? Artifact,
    string? Log);
