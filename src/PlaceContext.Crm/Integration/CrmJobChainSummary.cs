namespace PlaceContext.Crm.Integration;

public sealed record CrmJobChainSummary(
    Guid Id,
    Guid ProjectId,
    string Name,
    int StepCount,
    string? Description = null,
    IReadOnlyList<CrmJobChainStageSummary>? Stages = null);
