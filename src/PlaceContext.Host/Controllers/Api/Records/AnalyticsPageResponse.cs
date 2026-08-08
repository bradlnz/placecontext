namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record AnalyticsPageResponse(
    IReadOnlyList<AnalyticsTableResponse> Tables,
    IReadOnlyList<AnalyticsChartResponse> Charts,
    bool SweepPending,
    IReadOnlyList<string> PendingTables);
