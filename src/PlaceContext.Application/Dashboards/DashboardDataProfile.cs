using PlaceContext.Application.Features;

namespace PlaceContext.Application.Dashboards;

public enum DashboardConfidence { Unknown, Low, Medium, High }
public enum DashboardFreshness { Unknown, Current, Aging, Stale }
public enum DashboardCheckState { Pass, Caution, Attention, Unknown }

/// <summary>
/// Describes the evidence actually present in a saved chart. This is a bounded snapshot profile,
/// not a source-table audit or a model probability. Chart generation may replace missing source
/// values with zero and truncate rows; neither null rates nor dataset coverage can be recovered.
/// </summary>
public sealed record DashboardDataProfile(
    Guid ChartId, string Name, string? Source,
    DashboardConfidence Confidence, string ConfidenceReason,
    DashboardFreshness Freshness, DateTimeOffset GeneratedAt, TimeSpan? Age,
    int LabelCount, int SeriesCount, int ValueCount,
    IReadOnlyList<DashboardEvidenceCheck> Checks,
    IReadOnlyList<DashboardSeriesProfile> Series,
    IReadOnlyList<string> SuggestedQuestions)
{
    public static readonly TimeSpan CurrentWindow = TimeSpan.FromHours(24);
    public static readonly TimeSpan AgingWindow = TimeSpan.FromDays(7);
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(5);
    public const string Scope = "Saved chart values only. Source row counts, missing values, sampling and correctness are not verified.";
    private static readonly string[] SummaryCheckKeys = ["values", "labels", "measures"];

    public bool NeedsAttention => Confidence == DashboardConfidence.Unknown
        || Checks.Any(x => x.State == DashboardCheckState.Attention);
    public bool CanSummarize => SupportsComparison(Checks);

    public static DashboardDataProfile Create(DashboardChart chart, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var checks = new List<DashboardEvidenceCheck>();
        var age = chart.GeneratedAt == default || chart.GeneratedAt > now + ClockTolerance
            ? (TimeSpan?)null : now > chart.GeneratedAt ? now - chart.GeneratedAt : TimeSpan.Zero;
        var freshness = age is null ? DashboardFreshness.Unknown
            : age <= CurrentWindow ? DashboardFreshness.Current
            : age <= AgingWindow ? DashboardFreshness.Aging : DashboardFreshness.Stale;
        checks.Add(FreshnessCheck(freshness, chart.GeneratedAt, now));

        var rawLabels = chart.Spec?.Labels;
        var rawSeries = chart.Spec?.Series;
        var labels = rawLabels?.Take(ChartSpec.MaxLabels).Select(x => x ?? "").ToArray() ?? [];
        var seriesLimit = chart.Spec?.Type == "pie" ? 1 : ChartSpec.MaxSeries;
        var series = rawSeries?.Take(seriesLimit).ToArray() ?? [];
        var profiles = new List<DashboardSeriesProfile>();
        var invalidShape = labels.Length == 0 || series.Length == 0;
        var nonFinite = false;
        foreach (var item in series)
        {
            if (item?.Values is null) { invalidShape = true; continue; }
            if (item.Values.Count != (rawLabels?.Count ?? 0)) invalidShape = true;
            var points = item.Values.Take(labels.Length).Select((value, index) => (Value: value, Index: index)).ToArray();
            if (points.Any(x => !double.IsFinite(x.Value))) nonFinite = true;
            var finite = points.Where(x => double.IsFinite(x.Value)).ToArray();
            if (finite.Length == 0) continue;
            var min = finite.MinBy(x => x.Value);
            var max = finite.MaxBy(x => x.Value);
            profiles.Add(new(item.Name ?? "value", finite.Length, min.Value, max.Value,
                labels[min.Index], labels[max.Index], finite.Count(x => x.Value == 0), finite.Count(x => x.Value < 0)));
        }

        var valueCount = profiles.Sum(x => x.ValueCount);
        var valuesUsable = valueCount > 0 && !invalidShape && !nonFinite;
        checks.Add(new("values", "Displayed values", valuesUsable ? DashboardCheckState.Pass : DashboardCheckState.Attention,
            valuesUsable
                ? $"{valueCount} finite values across {series.Length} series. This checks chart structure, not source accuracy."
                : "The snapshot has missing, mismatched or non-finite chart values.",
            valuesUsable ? "Inspect the source query to verify how values were derived."
                : "Regenerate the chart from its source and review the query output."));

        var blankLabels = labels.Count(string.IsNullOrWhiteSpace);
        var duplicateLabels = labels.Where(x => !string.IsNullOrWhiteSpace(x)).GroupBy(x => x, StringComparer.Ordinal).Count(x => x.Count() > 1);
        var ambiguousLabels = blankLabels > 0 || duplicateLabels > 0;
        checks.Add(new("labels", "Category labels", labels.Length == 0 ? DashboardCheckState.Unknown
                : ambiguousLabels ? DashboardCheckState.Attention : DashboardCheckState.Pass,
            labels.Length == 0 ? "No category labels are available."
                : ambiguousLabels ? $"{blankLabels} blank labels and {duplicateLabels} repeated labels make comparisons ambiguous."
                : $"{labels.Length} named, distinct categories are visible. These are chart categories, not source record counts.",
            ambiguousLabels ? "Use distinct labels or group duplicate categories in the source query."
                : "Check which source field defines these categories."));

        var blankMeasures = series.Count(x => string.IsNullOrWhiteSpace(x?.Name));
        var duplicateMeasures = series.OfType<ChartSeries>().Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase).Count(x => x.Count() > 1);
        var ambiguousMeasures = blankMeasures > 0 || duplicateMeasures > 0;
        checks.Add(new("measures", "Measure names", series.Length == 0 ? DashboardCheckState.Unknown
                : ambiguousMeasures ? DashboardCheckState.Attention : DashboardCheckState.Pass,
            series.Length == 0 ? "No measures are available."
                : ambiguousMeasures ? $"{blankMeasures} blank measure names and {duplicateMeasures} repeated measure names make comparisons ambiguous."
                : $"{series.Length} named, distinct measures are visible.",
            ambiguousMeasures ? "Give each measure a distinct name in the source chart before comparing or asking questions."
                : "Confirm what each measure represents in the source query."));

        var atLimit = (rawLabels?.Count ?? 0) >= ChartSpec.MaxLabels || (rawSeries?.Count ?? 0) >= seriesLimit;
        checks.Add(new("coverage", "Source coverage", DashboardCheckState.Caution,
            atLimit
                ? "A chart display limit is reached. Source row count, sampling, omitted categories and missing values are not recorded."
                : "Source row count, sampling, omitted categories and missing values are not recorded in this snapshot.",
            "Check the source query and total row count before treating this view as the whole dataset."));
        checks.Add(new("source", "Source reference", string.IsNullOrWhiteSpace(chart.Source) ? DashboardCheckState.Unknown : DashboardCheckState.Pass,
            string.IsNullOrWhiteSpace(chart.Source) ? "This chart has no recorded source reference."
                : $"Stored source reference: {chart.Source}. The collection method and original query are not included.",
            "Open Analytics to inspect and regenerate the source chart."));

        // Current charts cannot earn High: the stored chart contract has no trustworthy coverage
        // or source-quality metadata. An AI response must never promote this confidence band.
        var confidence = valueCount == 0 ? DashboardConfidence.Unknown
            : checks.Any(x => x.State == DashboardCheckState.Attention) || freshness == DashboardFreshness.Unknown
                ? DashboardConfidence.Low : DashboardConfidence.Medium;
        var reason = confidence switch
        {
            DashboardConfidence.Unknown => "There are no usable displayed values to assess.",
            DashboardConfidence.Low => checks.FirstOrDefault(x => x.State == DashboardCheckState.Attention)?.Reason
                ?? "Snapshot freshness cannot be established.",
            _ => "Displayed values are usable, but unverified source coverage caps confidence at Medium."
        };

        var questions = new List<string>();
        foreach (var item in profiles.Take(SupportsComparison(checks) ? 2 : 0))
        {
            if (labels.Length > 1)
                questions.Add($"In the displayed snapshot of {Quote(chart.Name)}, is {Quote(item.MaximumLabel)} the only category with the highest {Quote(item.Name)} value?");
            questions.Add($"In the displayed snapshot of {Quote(chart.Name)}, are all {Quote(item.Name)} values greater than zero?");
        }
        return new(chart.Id, chart.Name, chart.Source, confidence, reason, freshness, chart.GeneratedAt, age,
            labels.Length, series.Length, valueCount, checks, profiles, questions.Distinct().Take(3).ToArray());
    }

    private static bool SupportsComparison(IReadOnlyList<DashboardEvidenceCheck> checks)
        => SummaryCheckKeys.All(key => checks.Any(x => x.Key == key && x.State == DashboardCheckState.Pass));

    private static string Quote(string value)
    {
        var text = value.Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ').Trim();
        return $"\"{(text.Length > 80 ? text[..80] + "…" : text)}\"";
    }

    private static DashboardEvidenceCheck FreshnessCheck(DashboardFreshness freshness, DateTimeOffset generatedAt, DateTimeOffset now)
        => freshness switch
        {
            DashboardFreshness.Current => new("freshness", "Snapshot age", DashboardCheckState.Pass,
                "Generated within 24 hours. This is chart generation time, not the age of the underlying records.",
                "Confirm the source's own update cadence before making time-sensitive decisions."),
            DashboardFreshness.Aging => new("freshness", "Snapshot age", DashboardCheckState.Caution,
                "Generated more than 24 hours and no more than 7 days ago.",
                "Regenerate the source chart in Analytics if the decision needs current data."),
            DashboardFreshness.Stale => new("freshness", "Snapshot age", DashboardCheckState.Attention,
                "Generated more than 7 days ago; current conditions may have changed.",
                "Regenerate the source chart in Analytics, then reload the dashboard snapshots."),
            _ => new("freshness", "Snapshot age", DashboardCheckState.Attention,
                generatedAt > now + ClockTolerance ? "The generation timestamp is more than 5 minutes in the future."
                    : "No usable generation timestamp is recorded.",
                "Check the source clock and regenerate the chart.")
        };
}
