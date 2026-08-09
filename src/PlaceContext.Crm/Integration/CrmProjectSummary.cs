namespace PlaceContext.Crm.Integration;

public sealed record CrmProjectSummary(
    Guid Id,
    string Name,
    string Path,
    string Status,
    bool IsGraphified,
    int GodNodeCount,
    int NodeCount,
    int LinkCount);
