namespace PlaceContext.Crm.Integration;

public sealed record CrmJobSummary(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    string ReturnType,
    IReadOnlyList<CrmJobParameterSummary> Parameters);
