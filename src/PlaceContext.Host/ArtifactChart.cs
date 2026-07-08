using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PlaceContext.Host;

/// <summary>
/// Jobs exist to generate artifacts — and when an artifact is JSON carrying a numeric series, the
/// portal renders it as an inline SVG bar chart beside the raw payload (the TUI does the same in
/// ASCII; keep the shape rules in sync with deploy/tui/artchart.go). Shapes understood:
/// <code>
///   [3, 1, 4]                                    → indexed bars
///   [{"day":"mon","total":12}, …]                → label field + value field per object
///   {"mon":12, "tue":31}                         → key/value bars (sorted by key)
///   {"data":[…]} / {"series":[…]} / {"rows":[…]} → unwrapped one level, then as above
/// </code>
/// Anything else renders nothing — the raw artifact block is always shown regardless.
/// Colours are the portal's own theme tokens (var(--brand) bars, text tokens for ink), so the
/// chart follows light/dark automatically; #8b7cff/#6b53ff pass the palette checks on both surfaces.
/// </summary>
public static class ArtifactChart
{
    private const int MaxRows = 16;

    private static readonly string[] LabelKeys = { "label", "name", "key", "date", "day", "month", "hour", "id", "title" };
    private static readonly string[] ValueKeys = { "value", "count", "total", "amount", "sum", "avg", "n", "qty" };

    /// <summary>Try to render <paramref name="json"/> as a bar-chart SVG. False when it isn't a numeric series.</summary>
    public static bool TrySvg(string? json, out string svg)
    {
        svg = "";
        var series = ExtractSeries(json);
        if (series is not { Count: >= 2 })
            return false;
        svg = RenderSvg(series);
        return true;
    }

    internal static List<(string Label, double Value)>? ExtractSeries(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        var t = json.TrimStart();
        if (t.Length == 0 || (t[0] != '{' && t[0] != '['))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Extract(doc.RootElement, depth: 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<(string, double)>? Extract(JsonElement el, int depth)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Array when el.GetArrayLength() > 0:
            {
                var first = el[0];
                if (first.ValueKind == JsonValueKind.Number)
                {
                    var series = new List<(string, double)>();
                    var i = 0;
                    foreach (var e in el.EnumerateArray())
                    {
                        if (e.ValueKind != JsonValueKind.Number) return null;
                        series.Add((i.ToString(CultureInfo.InvariantCulture), e.GetDouble()));
                        i++;
                    }
                    return series;
                }
                if (first.ValueKind == JsonValueKind.Object)
                    return FromObjectRows(el);
                return null;
            }
            case JsonValueKind.Object:
            {
                if (NumericMap(el) is { } map)
                    return map;
                if (depth < 1)
                {
                    // container object: unwrap the first array-valued property (sorted for stability)
                    foreach (var p in el.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                        if (p.Value.ValueKind == JsonValueKind.Array)
                            if (Extract(p.Value, depth + 1) is { Count: > 0 } inner)
                                return inner;
                }
                return null;
            }
            default:
                return null;
        }
    }

    private static List<(string, double)>? NumericMap(JsonElement obj)
    {
        var series = new List<(string, double)>();
        foreach (var p in obj.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Number) return null;
            series.Add((p.Name, p.Value.GetDouble()));
        }
        if (series.Count == 0) return null;
        series.Sort(static (a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return series;
    }

    private static List<(string, double)>? FromObjectRows(JsonElement rows)
    {
        var first = rows[0];
        var labelField = PickField(first, LabelKeys, JsonValueKind.String);
        var valueField = PickField(first, ValueKeys, JsonValueKind.Number);
        if (valueField is null)
            return null;

        var series = new List<(string, double)>();
        var i = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object
                || !row.TryGetProperty(valueField, out var v) || v.ValueKind != JsonValueKind.Number)
                return null; // ragged rows: not a series
            var label = i.ToString(CultureInfo.InvariantCulture);
            if (labelField is not null && row.TryGetProperty(labelField, out var l) && l.ValueKind == JsonValueKind.String)
                label = l.GetString() ?? label;
            series.Add((label, v.GetDouble()));
            i++;
        }
        return series;
    }

    private static string? PickField(JsonElement row, string[] preferred, JsonValueKind kind)
    {
        foreach (var k in preferred)
            if (row.TryGetProperty(k, out var v) && v.ValueKind == kind)
                return k;
        return row.EnumerateObject()
            .Where(p => p.Value.ValueKind == kind)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => (string?)p.Name)
            .FirstOrDefault();
    }

    // ── SVG rendering ─────────────────────────────────────────────────────────────────────────
    // Horizontal bars: label column (text-2 ink) · thin 14px bars in var(--brand) with a 4px
    // rounded data-end and 8px row gaps · value in mono at the data end (text ink) · a recessive
    // baseline in var(--border-2). Negative values chart by magnitude with the sign kept on the
    // label — a full diverging treatment isn't warranted for ad-hoc job output.
    private static string RenderSvg(List<(string Label, double Value)> series)
    {
        var shown = Math.Min(series.Count, MaxRows);
        const int rowPitch = 22, barH = 14, width = 480, padX = 4, valueW = 52;

        var labelW = 0;
        for (var i = 0; i < shown; i++)
            labelW = Math.Max(labelW, Math.Min(series[i].Label.Length, 16));
        var labelPx = Math.Min(12 + labelW * 7, 120);
        var barArea = width - padX - labelPx - valueW - 8;

        var maxAbs = 0d;
        for (var i = 0; i < shown; i++)
            maxAbs = Math.Max(maxAbs, Math.Abs(series[i].Value));

        var height = shown * rowPitch + 8 + (series.Count > shown ? 16 : 0);
        var sb = new StringBuilder();
        sb.Append($"<svg viewBox=\"0 0 {width} {height}\" role=\"img\" aria-label=\"bar chart generated from the artifact's JSON data\" ")
          .Append("style=\"width:100%;max-width:560px;display:block;margin:6px 0 2px\">");
        sb.Append("<style>.pcab:hover{opacity:.8}</style>");
        // baseline the bars grow from
        sb.Append($"<line x1=\"{labelPx + 4}\" y1=\"2\" x2=\"{labelPx + 4}\" y2=\"{shown * rowPitch + 4}\" stroke=\"var(--border-2)\" stroke-width=\"1\"/>");

        for (var i = 0; i < shown; i++)
        {
            var (label, value) = series[i];
            var y = 4 + i * rowPitch;
            var w = maxAbs > 0 ? (int)Math.Round(Math.Abs(value) / maxAbs * barArea) : 0;
            if (w == 0 && value != 0) w = 2; // nonzero values always get a visible bar
            var x0 = labelPx + 6;
            var display = label.Length > 16 ? label[..15] + "…" : label;

            sb.Append("<g class=\"pcab\">")
              .Append($"<title>{Esc(label)}: {Fmt(value)}</title>")
              .Append($"<text x=\"{labelPx}\" y=\"{y + barH - 3}\" text-anchor=\"end\" fill=\"var(--text-2)\" font-size=\"11\" font-family=\"'Geist',system-ui,sans-serif\">{Esc(display)}</text>");
            if (w >= 5)
                sb.Append($"<path d=\"M{x0},{y} h{w - 4} a4,4 0 0 1 4,4 v{barH - 8} a4,4 0 0 1 -4,4 h-{w - 4} z\" fill=\"var(--brand)\"/>");
            else if (w > 0)
                sb.Append($"<rect x=\"{x0}\" y=\"{y}\" width=\"{w}\" height=\"{barH}\" fill=\"var(--brand)\"/>");
            sb.Append($"<text x=\"{x0 + w + 6}\" y=\"{y + barH - 3}\" fill=\"var(--text)\" font-size=\"11\" font-family=\"'Geist Mono',monospace\">{Fmt(value)}</text>")
              .Append("</g>");
        }
        if (series.Count > shown)
            sb.Append($"<text x=\"{labelPx + 6}\" y=\"{shown * rowPitch + 16}\" fill=\"var(--text-3)\" font-size=\"11\" font-family=\"'Geist',system-ui,sans-serif\">… {series.Count - shown} more</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string Fmt(double v) =>
        v == Math.Truncate(v) && Math.Abs(v) < 1e12
            ? ((long)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
