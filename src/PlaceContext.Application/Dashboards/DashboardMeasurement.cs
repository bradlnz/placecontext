using PlaceContext.Application.Features;

namespace PlaceContext.Application.Dashboards;

/// <summary>Arithmetic over displayed chart values only, not unseen source rows or a time trend.</summary>
public sealed record DashboardMeasurement(int Count, double Minimum, double Maximum, double Mean,
    double Median, double Range, string MinimumLabel, string MaximumLabel)
{
    public static DashboardMeasurement? Create(ChartSeries? series, IReadOnlyList<string>? labels)
    {
        if (labels is not { Count: > 0 } || series?.Values is not { Count: > 0 }
            || series.Values.Count != labels.Count) return null;
        var values = series.Values.Take(Math.Min(labels.Count, ChartSpec.MaxLabels)).ToArray();
        if (values.Any(value => !double.IsFinite(value))) return null;
        var ordered = values.Order().ToArray();
        var minimum = ordered[0];
        var maximum = ordered[^1];
        var range = maximum - minimum;
        var scale = Math.Max(Math.Abs(minimum), Math.Abs(maximum));
        // Keep ordinary-scale sums intact (e.g. 192 / 10 == the entered target 19.2).
        // Normalize only when the sum overflows so large finite values still have a usable mean.
        var sum = Sum(values);
        var mean = double.IsFinite(sum) ? sum / values.Length
            : scale == 0 ? 0 : Math.Clamp(Sum(values.Select(value => value / scale)) / values.Length, -1, 1) * scale;
        var middle = values.Length / 2;
        var median = values.Length % 2 == 1 ? ordered[middle] : ordered[middle - 1] / 2 + ordered[middle] / 2;
        if (!double.IsFinite(range) || !double.IsFinite(mean) || !double.IsFinite(median)) return null;
        return new(values.Length, minimum, maximum, mean, median, range,
            labels[Array.IndexOf(values, minimum)], labels[Array.IndexOf(values, maximum)]);
    }

    public double Value(DashboardMeasureStatistic statistic) => statistic switch
    {
        DashboardMeasureStatistic.Minimum => Minimum,
        DashboardMeasureStatistic.Maximum => Maximum,
        DashboardMeasureStatistic.Mean => Mean,
        DashboardMeasureStatistic.Median => Median,
        _ => throw new ArgumentOutOfRangeException(nameof(statistic))
    };

    private static double Sum(IEnumerable<double> values)
    {
        var sum = 0d;
        var correction = 0d;
        foreach (var value in values)
        {
            var next = sum + value;
            if (!double.IsFinite(next)) return next;
            correction += Math.Abs(sum) >= Math.Abs(value) ? (sum - next) + value : (value - next) + sum;
            sum = next;
        }
        return sum + correction;
    }
}
