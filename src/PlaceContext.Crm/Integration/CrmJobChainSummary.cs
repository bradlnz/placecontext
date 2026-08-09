namespace PlaceContext.Crm.Integration;

public sealed record CrmJobChainSummary(
    Guid Id,
    Guid ProjectId,
    string Name,
    int StepCount);
