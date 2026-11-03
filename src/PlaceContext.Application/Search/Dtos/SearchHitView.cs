namespace PlaceContext.Application.Dtos;

public sealed record SearchHitView(
    string Index,
    string Id,
    double? Score,
    IReadOnlyDictionary<string, string?> Fields);
