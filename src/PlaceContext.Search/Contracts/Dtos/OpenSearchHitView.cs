namespace PlaceContext.Application.Dtos;

public sealed record OpenSearchHitView(
    string Index,
    string Id,
    double? Score,
    IReadOnlyDictionary<string, string?> Fields);
