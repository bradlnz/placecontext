namespace PlaceContext.Data.Integration;

public sealed record DataChainSummary(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    IReadOnlyList<DataChainStageSummary> Stages);
