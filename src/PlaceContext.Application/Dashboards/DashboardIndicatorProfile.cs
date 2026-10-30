using PlaceContext.Application.Features;

namespace PlaceContext.Application.Dashboards;

/// <summary>
/// An indicator profile groups *named measures* under their recorded source. It never
/// treats chart labels as customer IDs, joins records across sources, or infers trends.
/// Per-customer profiles require an explicit, verified customer identity and relationship.
/// </summary>
public sealed record DashboardIndicatorProfile(
    string Name, string? Source, IReadOnlyList<DashboardIndicator> Indicators,
    DashboardConfidence Confidence, int NeedsDataCount);

public sealed record DashboardIndicator(
    Guid ChartId, string ChartName, string SeriesName, DashboardMeasurement? Measurement,
    DashboardConfidence Confidence, DashboardFreshness Freshness, bool NeedsData,
    DashboardWatchTarget? Target)
{
    public string ObservedStatistic => Target?.Statistic.ToString() ?? "Mean";
    public double? ObservedValue => Measurement is null ? null
        : Target is { } target ? Measurement.Value(target.Statistic) : Measurement.Mean;
}

public static class DashboardIndicatorProfiles
{
    public static IReadOnlyList<DashboardIndicatorProfile> Create(
        DashboardSnapshot snapshot, DateTimeOffset assessedAt,
        IReadOnlyList<DashboardWatchTarget>? targets = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var entries = new List<(string Key, string Name, string? Source, DashboardIndicator Indicator)>();
        foreach (var chart in snapshot.Charts)
        {
            var source = string.IsNullOrWhiteSpace(chart.Source) ? null : chart.Source.Trim();
            // Without an explicit common source reference, unrelated charts must never be merged.
            var key = source is null ? $"chart:{chart.Id:N}" : $"source:{source}";
            var name = source ?? chart.Name;
            var dataProfile = DashboardDataProfile.Create(chart, assessedAt);
            var series = chart.Spec?.Series?.Take(chart.Spec.Type == "pie" ? 1 : ChartSpec.MaxSeries)
                .OfType<ChartSeries>().ToArray() ?? [];
            foreach (var item in series)
            {
                if (string.IsNullOrWhiteSpace(item.Name)) continue;
                var target = targets?.FirstOrDefault(x => x.ChartId == chart.Id
                    && string.Equals(x.SeriesName, item.Name, StringComparison.OrdinalIgnoreCase));
                var measurement = DashboardMeasurement.Create(item, chart.Spec?.Labels);
                var needsData = !dataProfile.CanSummarize
                    || dataProfile.Freshness is DashboardFreshness.Stale or DashboardFreshness.Unknown
                    || measurement is null;
                entries.Add((key, name, source,
                    new(chart.Id, chart.Name, item.Name.Trim(), measurement,
                        dataProfile.Confidence, dataProfile.Freshness, needsData, target)));
            }
        }

        return entries.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var indicators = group.Select(x => x.Indicator).ToArray();
                var confidence = indicators.Length == 0 || indicators.Any(x => x.Confidence == DashboardConfidence.Unknown)
                    ? DashboardConfidence.Unknown
                    : indicators.Any(x => x.Confidence == DashboardConfidence.Low)
                        ? DashboardConfidence.Low : DashboardConfidence.Medium;
                return new DashboardIndicatorProfile(group.First().Name, group.First().Source,
                    indicators, confidence, indicators.Count(x => x.NeedsData));
            }).ToArray();
    }
}
