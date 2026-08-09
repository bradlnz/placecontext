namespace PlaceContext.Crm.Integration;

public sealed record CrmJobChainRun(
    Guid Id,
    Guid ChainId,
    string ChainName,
    string Status,
    IReadOnlyList<CrmJobChainStepRun> Steps,
    string? FinalOutput,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);
