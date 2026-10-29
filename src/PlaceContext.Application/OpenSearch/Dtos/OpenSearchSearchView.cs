namespace PlaceContext.Application.Dtos;

public sealed record OpenSearchSearchView(
    long Total,
    int TookMs,
    IReadOnlyList<OpenSearchHitView> Hits,
    string? ChartSpecJson);
