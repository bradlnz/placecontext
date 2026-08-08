namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record JobChainRunStepResponse(
    int Index,
    int StageIndex,
    int BranchIndex,
    Guid JobId,
    string JobName,
    Guid? RunId,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ActionType,
    string? Provider,
    string? ExternalId,
    string? Error);
