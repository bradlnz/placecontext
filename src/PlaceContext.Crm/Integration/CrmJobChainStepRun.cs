namespace PlaceContext.Crm.Integration;

public sealed record CrmJobChainStepRun(
    int Index,
    int StageIndex,
    int BranchIndex,
    Guid JobId,
    string JobName,
    Guid? RunId,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error);
