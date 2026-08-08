namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record ProjectPageOverviewResponse(
    Guid Id,
    string Name,
    string Path,
    string Status,
    IReadOnlyList<ProjectPageGodNodeResponse> GodNodes);
