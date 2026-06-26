using System.Text;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Pure builders that turn a finished <see cref="JobRun"/> into post-job output bytes — a self-contained
/// HTML report, an offline inline-SVG chart, a CSV, and the raw output files. No I/O, no domain logic
/// about <i>what</i> the job does: artifacts are treated as opaque text.
/// </summary>
public static class PostJobArtifacts
{
    public sealed record BuiltFile(string FileName, byte[] Content, string ContentType, string Title);

    // ── HTML report ───────────────────────────────────────────────────────────────────────────────
    public static BuiltFile HtmlReport(Job job, JobRun run)
    {
        var sb = new StringBuilder();
        sb.Append(HtmlHead($"{job.Name} — run {Short(run.Id)}"));
        sb.Append($"<h1>{Esc(job.Name)}</h1>");
        sb.Append($"<p class=meta>Run <code>{run.Id:N}</code> · status <b class=\"s-{run.Status}\">{run.Status}</b>");
        if (run.FinishedAt is { } f) sb.Append($" · finished {f:yyyy-MM-dd HH:mm} UTC");
        sb.Append($" · {run.ShardResults.Count} shard(s)</p>");

        sb.Append(OutcomeChartSvg(run)); // embed the same chart at the top of the report

        foreach (var s in run.ShardResults)
        {
            sb.Append($"<section><h2>Shard {s.Index} <span class=\"badge o-{s.Outcome}\">{s.Outcome}</span> <span class=exit>exit {s.ExitCode}</span></h2>");
            if (!string.IsNullOrWhiteSpace(s.Artifact)) sb.Append($"<h3>artifact</h3><pre>{Esc(s.Artifact!)}</pre>");
            if (!string.IsNullOrWhiteSpace(s.Log)) sb.Append($"<details><summary>console (stdout/stderr)</summary><pre class=log>{Esc(s.Log!)}</pre></details>");
            foreach (var a in s.Artifacts) sb.Append($"<h3>file: {Esc(a.Name)}</h3><pre>{Esc(a.Content)}</pre>");
            sb.Append("</section>");
        }
        if (run.ReduceResult is { } r)
        {
            sb.Append($"<section><h2>Reduce <span class=\"badge\">{(r.Succeeded ? "succeeded" : "failed")}</span> <span class=exit>exit {r.ExitCode}</span></h2>");
            if (!string.IsNullOrWhiteSpace(r.Artifact)) sb.Append($"<h3>artifact</h3><pre>{Esc(r.Artifact!)}</pre>");
            foreach (var a in r.Artifacts) sb.Append($"<h3>file: {Esc(a.Name)}</h3><pre>{Esc(a.Content)}</pre>");
            sb.Append("</section>");
        }
        sb.Append("</body></html>");
        return new BuiltFile("report.html", Bytes(sb), "text/html; charset=utf-8", "HTML report");
    }

    // ── Chart (standalone HTML wrapping the inline SVG) ─────────────────────────────────────────────
    public static BuiltFile Chart(Job job, JobRun run)
    {
        var sb = new StringBuilder();
        sb.Append(HtmlHead($"{job.Name} — chart"));
        sb.Append($"<h1>{Esc(job.Name)} — shard outcomes</h1>");
        sb.Append(OutcomeChartSvg(run));
        sb.Append("</body></html>");
        return new BuiltFile("chart.html", Bytes(sb), "text/html; charset=utf-8", "Chart");
    }

    // ── CSV ─────────────────────────────────────────────────────────────────────────────────────────
    public static BuiltFile Csv(JobRun run)
    {
        var sb = new StringBuilder();
        sb.Append("shard,exit_code,outcome,artifact\n");
        foreach (var s in run.ShardResults)
            sb.Append($"{s.Index},{s.ExitCode},{s.Outcome},{CsvField(s.Artifact ?? "")}\n");
        if (run.ReduceResult is { } r)
            sb.Append($"reduce,{r.ExitCode},{(r.Succeeded ? "Succeeded" : "Failed")},{CsvField(r.Artifact ?? "")}\n");
        return new BuiltFile("run.csv", Bytes(sb), "text/csv; charset=utf-8", "CSV export");
    }

    // ── Raw bundle: each produced file, as-is ───────────────────────────────────────────────────────
    public static IEnumerable<BuiltFile> RawBundle(JobRun run)
    {
        foreach (var s in run.ShardResults)
        {
            if (!string.IsNullOrEmpty(s.Artifact))
                yield return RawFile($"raw/shard-{s.Index}/result.json", s.Artifact!);
            foreach (var a in s.Artifacts)
                yield return RawFile($"raw/shard-{s.Index}/{a.Name}", a.Content);
        }
        if (run.ReduceResult is { } r)
        {
            if (!string.IsNullOrEmpty(r.Artifact))
                yield return RawFile("raw/reduce/result.json", r.Artifact!);
            foreach (var a in r.Artifacts)
                yield return RawFile($"raw/reduce/{a.Name}", a.Content);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────
    private static BuiltFile RawFile(string path, string content)
        => new(path, Encoding.UTF8.GetBytes(content), ContentTypeFor(path), path[(path.LastIndexOf('/') + 1)..]);

    // A horizontal bar chart of shard outcomes as inline SVG — self-contained, opens offline.
    private static string OutcomeChartSvg(JobRun run)
    {
        var counts = new (WorkloadOutcome O, string Color)[]
        {
            (WorkloadOutcome.Succeeded, "#16a34a"),
            (WorkloadOutcome.Partial, "#d97706"),
            (WorkloadOutcome.Failed, "#dc2626"),
        };
        var data = counts.Select(c => (c.O, c.Color, N: run.ShardResults.Count(s => s.Outcome == c.O))).ToArray();
        var maxN = Math.Max(1, data.Max(d => d.N));
        const int w = 420, rowH = 34, barMax = 300, x0 = 90;
        var h = data.Length * rowH + 16;
        var sb = new StringBuilder();
        sb.Append($"<svg width=\"{w}\" height=\"{h}\" viewBox=\"0 0 {w} {h}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\">");
        for (var i = 0; i < data.Length; i++)
        {
            var (o, color, n) = data[i];
            var y = i * rowH + 8;
            var bw = (int)Math.Round((double)n / maxN * barMax);
            sb.Append($"<text x=\"{x0 - 8}\" y=\"{y + 16}\" text-anchor=\"end\" font-family=\"sans-serif\" font-size=\"13\" fill=\"#444\">{o}</text>");
            sb.Append($"<rect x=\"{x0}\" y=\"{y}\" width=\"{Math.Max(bw, 1)}\" height=\"22\" rx=\"3\" fill=\"{color}\"/>");
            sb.Append($"<text x=\"{x0 + Math.Max(bw, 1) + 6}\" y=\"{y + 16}\" font-family=\"sans-serif\" font-size=\"13\" fill=\"#444\">{n}</text>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string HtmlHead(string title) =>
        "<!doctype html><html><head><meta charset=utf-8><meta name=viewport content=\"width=device-width,initial-scale=1\">" +
        $"<title>{Esc(title)}</title><style>" +
        "body{font-family:ui-sans-serif,system-ui,sans-serif;max-width:920px;margin:24px auto;padding:0 18px;color:#1f2937}" +
        "h1{font-size:22px}h2{font-size:16px;margin:18px 0 6px}h3{font-size:12px;color:#6b7280;margin:10px 0 4px;text-transform:uppercase;letter-spacing:.03em}" +
        ".meta{color:#6b7280;font-size:13px}code{font-family:ui-monospace,monospace;font-size:12px}" +
        "pre{background:#0b10180a;border:1px solid #e5e7eb;border-radius:8px;padding:10px;font-size:12px;overflow:auto;white-space:pre-wrap;word-break:break-word}" +
        "pre.log{color:#6b7280}section{border-top:1px solid #eef2f7;padding-top:6px}" +
        ".badge{font-size:11px;padding:2px 8px;border-radius:6px;background:#eef2f7;color:#374151}" +
        ".o-Succeeded{background:#dcfce7;color:#166534}.o-Partial{background:#fef3c7;color:#92400e}.o-Failed{background:#fee2e2;color:#991b1b}" +
        ".exit{font-size:11px;color:#9ca3af}.s-Succeeded{color:#166534}.s-Failed{color:#991b1b}.s-Partial{color:#92400e}" +
        "</style></head><body>";

    private static byte[] Bytes(StringBuilder sb) => Encoding.UTF8.GetBytes(sb.ToString());
    private static string Short(Guid id) => id.ToString("N")[..8];

    private static string Esc(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string CsvField(string s)
    {
        var v = s.Replace("\r", " ").Replace("\n", " ");
        if (v.Contains(',') || v.Contains('"')) v = "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }

    private static string ContentTypeFor(string path)
    {
        var ext = path[(path.LastIndexOf('.') + 1)..].ToLowerInvariant();
        return ext switch
        {
            "json" => "application/json",
            "csv" => "text/csv; charset=utf-8",
            "html" or "htm" => "text/html; charset=utf-8",
            "svg" => "image/svg+xml",
            "txt" or "log" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };
    }
}
