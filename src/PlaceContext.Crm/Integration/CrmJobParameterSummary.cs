namespace PlaceContext.Crm.Integration;

public sealed record CrmJobParameterSummary(
    string Name,
    string? Label,
    bool Required,
    string Type,
    IReadOnlyList<string>? Options);
