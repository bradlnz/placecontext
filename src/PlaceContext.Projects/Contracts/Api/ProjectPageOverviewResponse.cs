namespace PlaceContext.Projects.Api;

public sealed record ProjectPageOverviewResponse(
    Guid Id,
    string Name,
    string Path,
    string Status,
    IReadOnlyList<ProjectPageGodNodeResponse> GodNodes);
