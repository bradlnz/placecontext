namespace PlaceContext.Data.Contracts.Api;

public sealed record AnalyticsPageResponse(
    IReadOnlyList<AnalyticsTableResponse> Tables,
    IReadOnlyList<AnalyticsChartResponse> Charts,
    bool SweepPending,
    IReadOnlyList<string> PendingTables);
