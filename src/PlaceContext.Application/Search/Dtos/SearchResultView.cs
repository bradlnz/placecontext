namespace PlaceContext.Application.Dtos;

public sealed record SearchResultView(
    long Total,
    int TookMs,
    IReadOnlyList<SearchHitView> Hits,
    string? ChartSpecJson);
