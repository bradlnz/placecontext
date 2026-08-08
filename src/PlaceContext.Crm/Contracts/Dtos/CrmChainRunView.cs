namespace PlaceContext.Application.Features;

public sealed record CrmChainRunView(
    Guid Id,
    Guid ClientId,
    Guid ChainId,
    string ChainName,
    Guid ChainRunId,
    string LifecycleStage,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);
