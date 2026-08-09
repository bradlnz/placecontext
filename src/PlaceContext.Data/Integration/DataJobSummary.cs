namespace PlaceContext.Data.Integration;

public sealed record DataJobSummary(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    string ReturnType,
    bool AllowApiInvocation = false);
