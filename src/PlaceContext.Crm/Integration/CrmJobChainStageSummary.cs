namespace PlaceContext.Crm.Integration;

public sealed record CrmJobChainStageSummary(
    IReadOnlyList<CrmJobChainStageJobSummary> Jobs,
    string? ConditionExpression);
