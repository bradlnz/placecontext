namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobChainResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    IReadOnlyList<JobChainStageResponse> Stages,
    DateTimeOffset UpdatedAt,
    string UpdatedAtDisplay);
