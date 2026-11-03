namespace PlaceContext.Host.Api;

public sealed record CoreJobSummaryResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    string MapSourceKind,
    string ReturnType,
    bool AllowApiInvocation,
    bool AllowNetworkEgress,
    DateTimeOffset UpdatedAt);
