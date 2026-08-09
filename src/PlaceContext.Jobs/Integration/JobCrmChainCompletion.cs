namespace PlaceContext.Jobs.Integration;

public sealed record JobCrmChainCompletion(
    Guid ProjectId,
    Guid ClientId,
    Guid ChainId,
    Guid ChainRunId,
    string ChainName,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<Guid> RunIds);
