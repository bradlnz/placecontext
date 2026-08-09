namespace PlaceContext.Crm.Contracts.Api;

public sealed record CrmChainCompletionRequest(
    Guid ProjectId,
    Guid ClientId,
    Guid ChainId,
    Guid ChainRunId,
    string ChainName,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<Guid> RunIds);
