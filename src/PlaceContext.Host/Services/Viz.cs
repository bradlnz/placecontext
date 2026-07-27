namespace PlaceContext.Host;

/// <summary>
/// Presentation helpers for the portal: a deterministic graph layout (so real nodes get stable
/// positions without a layout engine, mirroring the design mockup's seeded generator) plus the
/// colour/format mappings the views share.
/// </summary>
public static class Viz
{
    /// <summary>Deterministic 2-D positions in a 100×66 viewBox, seeded so a project's graph is stable.</summary>
    public static (double X, double Y)[] Layout(int count, int seed, double w = 100, double h = 66)
    {
        var pts = new (double, double)[Math.Max(0, count)];
        long s = (long)seed * 99991 + 7;
        double Rnd()
        {
            s = (s * 1103515245 + 12345) & 0x7fffffff;
            return ((s >> 8) & 0x7fffff) / (double)0x7fffff;
        }
        for (var i = 0; i < pts.Length; i++)
            pts[i] = (6 + Rnd() * (w - 12), 6 + Rnd() * (h - 12));
        return pts;
    }

    /// <summary>Stable integer seed from a project id (no Math.Random — deterministic across renders).</summary>
    public static int Seed(Guid id) => Math.Abs(id.GetHashCode()) % 100000 + 1;

    public static string AuthorColor(string kind) => kind == "Agent" ? "var(--brand-2)" : "var(--human)";
    public static string AuthorBg(string kind) => kind == "Agent" ? "var(--brand-bg)" : "var(--human-bg)";

    public static string ConfidenceStroke(string c) => c switch
    {
        "Extracted" => "var(--border-2)",
        "Ambiguous" => "var(--warn)",
        _ => "var(--human)"
    };

    public static string LangColor(string path)
    {
        // Heuristic accent from the project's dominant marker — purely decorative.
        if (path.EndsWith(".rs")) return "#e3651f";
        return "#8b7cff";
    }

    public static string Ago(DateTimeOffset when)
    {
        var span = DateTimeOffset.UtcNow - when;
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }
}
