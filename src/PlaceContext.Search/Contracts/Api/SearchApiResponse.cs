namespace PlaceContext.Search.Contracts.Api;

public sealed record SearchApiResponse(
    string Query,
    Guid ProjectId,
    int Count,
    IReadOnlyList<SearchApiHitResponse> Hits);
