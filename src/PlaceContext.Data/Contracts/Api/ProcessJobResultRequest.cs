namespace PlaceContext.Data.Contracts.Api;

public sealed record ProcessJobResultRequest(
    string SourceKind,
    Guid SourceId,
    Guid RunId,
    Guid ProjectId,
    string? PrimaryOutput,
    IReadOnlyList<JobResultDocument> Documents);
