using System.Text.Json;
using System.Text.Json.Serialization;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>
/// A declarative chart: data + form, no drawing. The LLM (or the deterministic builder) produces
/// this and the portal renders it with Chart.js — code draws, the model only picks and shapes data,
/// which it can do reliably where hand-drawn SVG could not.
/// </summary>
public sealed record ChartSpec(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("labels")] IReadOnlyList<string> Labels,
    [property: JsonPropertyName("series")] IReadOnlyList<ChartSeries> Series)
{
    public const int MaxLabels = 24;
    public const int MaxSeries = 4;
    private static readonly string[] Types = { "bar", "line", "pie" };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Serialized form stored in the chart row; starts with '{' which is how the portal
    /// tells a spec from a legacy HTML chart.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// Parses and validates a chart spec from (possibly fenced / prose-wrapped) model output.
    /// Returns null when the payload isn't a usable chart: unknown type, no labels, no series,
    /// non-finite numbers, or series lengths that don't match the labels.
    /// </summary>
    public static ChartSpec? TryParse(string raw)
    {
        var json = ExtractJson(raw);
        if (json is null) return null;

        ChartSpec? spec;
        try { spec = JsonSerializer.Deserialize<ChartSpec>(json, Json); }
        catch (JsonException) { return null; }
        if (spec is null) return null;

        var type = (spec.Type ?? "").Trim().ToLowerInvariant();
        if (!Types.Contains(type)) return null;
        if (spec.Labels is not { Count: > 0 } || spec.Series is not { Count: > 0 }) return null;

        var labels = spec.Labels.Take(MaxLabels).Select(l => l ?? "").ToList();
        var seriesLimit = type == "pie" ? 1 : MaxSeries;
        var series = new List<ChartSeries>(seriesLimit);
        foreach (var s in spec.Series.Take(seriesLimit))
        {
            if (s?.Values is not { Count: > 0 }) return null;
            if (s.Values.Count < labels.Count) return null;
            var values = s.Values.Take(labels.Count).ToList();
            if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v))) return null;
            series.Add(new ChartSeries(string.IsNullOrWhiteSpace(s.Name) ? "value" : s.Name.Trim(), values));
        }

        return new ChartSpec(type, string.IsNullOrWhiteSpace(spec.Title) ? null : spec.Title.Trim(), labels, series);
    }

    /// <summary>
    /// Deterministic spec from sampled rows — the no-LLM (or LLM-misbehaved) path. Numeric columns
    /// become series over the first text column's labels; a table with no numeric column becomes a
    /// bar chart of its leading column's value counts. Null when nothing chartable exists.
    /// </summary>
    public static ChartSpec? FromSample(string tableName, ProjectQueryResult result)
    {
        if (result.Columns.Count == 0 || result.Rows.Count == 0) return null;

        // Column classification: numeric when ≥80% of its non-empty cells parse as doubles.
        var numericCols = new List<int>();
        var labelCol = -1;
        for (var c = 0; c < result.Columns.Count; c++)
        {
            var cells = result.Rows.Select(r => c < r.Count ? r[c] : null)
                .Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            if (cells.Count == 0) continue;
            var parsed = cells.Count(v => double.TryParse(v, out _));
            if (parsed >= cells.Count * 0.8) numericCols.Add(c);
            else if (labelCol < 0) labelCol = c;
        }

        if (numericCols.Count > 0)
        {
            var rows = result.Rows.Take(MaxLabels).ToList();
            var labels = rows.Select((r, i) =>
                labelCol >= 0 && labelCol < r.Count && !string.IsNullOrWhiteSpace(r[labelCol])
                    ? r[labelCol]! : (i + 1).ToString()).ToList();
            var series = numericCols.Take(3).Select(c => new ChartSeries(
                result.Columns[c],
                rows.Select(r => c < r.Count && double.TryParse(r[c], out var v) ? v : 0d).ToList()
            )).ToList();
            return new ChartSpec("bar", tableName, labels, series);
        }

        if (labelCol >= 0)
        {
            var counts = result.Rows
                .Select(r => labelCol < r.Count ? (r[labelCol] ?? "∅") : "∅")
                .GroupBy(v => v)
                .OrderByDescending(g => g.Count())
                .Take(12)
                .ToList();
            if (counts.Count > 1)
                return new ChartSpec("bar", $"{tableName} — rows per {result.Columns[labelCol]}",
                    counts.Select(g => g.Key).ToList(),
                    new[] { new ChartSeries("rows", counts.Select(g => (double)g.Count()).ToList()) });
        }

        return null;
    }

    // Model output arrives as bare JSON, fenced JSON, or JSON with prose around it — take the
    // fenced block when present, else the outermost braces.
    private static string? ExtractJson(string raw)
    {
        var s = (raw ?? "").Trim();
        var fence = s.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var afterTicks = s.IndexOf('\n', fence);
            if (afterTicks >= 0)
            {
                var rest = s[(afterTicks + 1)..];
                var close = rest.IndexOf("```", StringComparison.Ordinal);
                s = (close >= 0 ? rest[..close] : rest).Trim();
            }
        }
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        return start >= 0 && end > start ? s[start..(end + 1)] : null;
    }
}
