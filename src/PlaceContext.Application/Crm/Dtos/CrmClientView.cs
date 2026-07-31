namespace PlaceContext.Application.Features;

public sealed record CrmClientView(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Company,
    string? Email,
    string? Phone,
    string LifecycleStage,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

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
