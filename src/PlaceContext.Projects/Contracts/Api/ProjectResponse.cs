namespace PlaceContext.Projects.Api;

/// <summary>Stable machine-facing project read model.</summary>
public sealed record ProjectResponse(
    Guid Id,
    string Name,
    string Path,
    string Status,
    bool IsGraphified);
