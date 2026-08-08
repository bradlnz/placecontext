namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record JobChainResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    IReadOnlyList<JobChainStageResponse> Stages,
    DateTimeOffset UpdatedAt,
    string UpdatedAtDisplay);
