namespace PlaceContext.Host.Components.ViewModels;

public sealed record LogAnalyticsResult(IReadOnlyList<string> Columns, IReadOnlyList<LogAnalyticsRow> Rows,
    int TotalRows, int MatchedEvents, bool Aggregated)
{
    public IReadOnlyList<string> SourceFields { get; init; } = [];
}
