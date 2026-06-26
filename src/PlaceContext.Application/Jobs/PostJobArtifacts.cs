using System.Text;
using System.Text.Json;
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
    // Parses each shard's artifact as JSON and renders the *information* it carries (scalar fields as a
    // header, arrays of objects as cards/tables), not a report of the run. Falls back to a preformatted
    // block when an artifact isn't JSON. The reduce artifact (when present) is treated as the primary data.
    public static BuiltFile HtmlReport(Job job, JobRun run)
    {
        var sb = new StringBuilder();
        sb.Append(HtmlHead(job.Name));
        sb.Append($"<h1>{Esc(job.Name)}</h1>");
        if (run.FinishedAt is { } f) sb.Append($"<p class=meta>Generated {f:yyyy-MM-dd HH:mm} UTC</p>");

        // Prefer the reduce artifact (the job's final, aggregated output) if it exists; else each shard.
        var rendered = false;
        if (run.ReduceResult?.Artifact is { Length: > 0 } reduceArtifact)
        {
            sb.Append(RenderArtifactHtml(reduceArtifact));
            rendered = true;
        }
        else
        {
            foreach (var s in run.ShardResults)
            {
                if (string.IsNullOrWhiteSpace(s.Artifact)) continue;
                if (run.ShardResults.Count(x => !string.IsNullOrWhiteSpace(x.Artifact)) > 1)
                    sb.Append($"<h2>Shard {s.Index}</h2>");
                sb.Append(RenderArtifactHtml(s.Artifact!));
                rendered = true;
            }
        }
        if (!rendered) sb.Append("<p class=meta>This run produced no data artifact.</p>");

        sb.Append("</body></html>");
        return new BuiltFile("report.html", Bytes(sb), "text/html; charset=utf-8", "HTML report");
    }

    // ── data → HTML rendering (generic; the artifact content is opaque JSON) ─────────────────────────
    private const int MaxItems = 2000;
    private const int MaxDepth = 6;
    private static readonly string[] TitleKeys = { "title", "name", "headline", "label", "subject", "heading" };
    private static readonly string[] UrlKeys = { "url", "link", "href", "sourceurl", "permalink" };

    private static string RenderArtifactHtml(string artifact)
    {
        try
        {
            using var doc = JsonDocument.Parse(artifact);
            return RenderElement(doc.RootElement, 0);
        }
        catch
        {
            return $"<pre>{Esc(artifact)}</pre>"; // not JSON — show it raw
        }
    }

    private static string RenderElement(JsonElement el, int depth) => el.ValueKind switch
    {
        JsonValueKind.Object => RenderObject(el, depth),
        JsonValueKind.Array => RenderArray(el, depth),
        _ => "<p>" + Scalar(el) + "</p>",
    };

    private static string RenderObject(JsonElement obj, int depth)
    {
        var sb = new StringBuilder();
        var scalars = new List<JsonProperty>();
        var complex = new List<JsonProperty>();
        foreach (var p in obj.EnumerateObject())
            (p.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object ? complex : scalars).Add(p);

        if (scalars.Count > 0)
        {
            sb.Append("<dl class=kv>");
            foreach (var p in scalars)
            {
                if (p.Value.ValueKind is JsonValueKind.Null) continue;
                sb.Append($"<dt>{Esc(Humanize(p.Name))}</dt><dd>{Scalar(p.Value)}</dd>");
            }
            sb.Append("</dl>");
        }
        if (depth < MaxDepth)
            foreach (var p in complex)
            {
                sb.Append($"<h3>{Esc(Humanize(p.Name))}</h3>");
                sb.Append(RenderElement(p.Value, depth + 1));
            }
        return sb.ToString();
    }

    private static string RenderArray(JsonElement arr, int depth)
    {
        var items = arr.EnumerateArray().ToList();
        if (items.Count == 0) return "<p class=meta>(none)</p>";

        if (items[0].ValueKind == JsonValueKind.Object)
        {
            var sb = new StringBuilder("<div class=cards>");
            for (var i = 0; i < items.Count && i < MaxItems; i++) sb.Append(RenderCard(items[i]));
            if (items.Count > MaxItems) sb.Append($"<p class=meta>… and {items.Count - MaxItems} more</p>");
            sb.Append("</div>");
            return sb.ToString();
        }

        var ul = new StringBuilder("<ul>");
        foreach (var it in items.Take(MaxItems)) ul.Append($"<li>{Scalar(it)}</li>");
        ul.Append("</ul>");
        return ul.ToString();
    }

    private static string RenderCard(JsonElement obj)
    {
        string? title = null, url = null;
        foreach (var p in obj.EnumerateObject())
        {
            var lk = p.Name.ToLowerInvariant();
            if (title is null && p.Value.ValueKind == JsonValueKind.String && TitleKeys.Contains(lk)) title = p.Value.GetString();
            if (url is null && p.Value.ValueKind == JsonValueKind.String && UrlKeys.Contains(lk)) url = p.Value.GetString();
        }

        var sb = new StringBuilder("<div class=card>");
        if (!string.IsNullOrWhiteSpace(title))
            sb.Append(url is not null
                ? $"<div class=ctitle><a href=\"{EscAttr(url)}\" target=_blank rel=noopener>{Esc(title!)}</a></div>"
                : $"<div class=ctitle>{Esc(title!)}</div>");

        sb.Append("<dl class=kv>");
        foreach (var p in obj.EnumerateObject())
        {
            var lk = p.Name.ToLowerInvariant();
            if (TitleKeys.Contains(lk) || UrlKeys.Contains(lk)) continue; // already shown as the card title/link
            if (p.Value.ValueKind is JsonValueKind.Null) continue;
            var val = p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? Esc(Compact(p.Value)) : Scalar(p.Value);
            sb.Append($"<dt>{Esc(Humanize(p.Name))}</dt><dd>{val}</dd>");
        }
        sb.Append("</dl></div>");
        return sb.ToString();
    }

    // A scalar value as HTML — URLs become links.
    private static string Scalar(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString() ?? "";
            if (s.StartsWith("http://") || s.StartsWith("https://"))
                return $"<a href=\"{EscAttr(s)}\" target=_blank rel=noopener>{Esc(s)}</a>";
            return Esc(s);
        }
        return Esc(v.ToString());
    }

    private static string Compact(JsonElement v)
    {
        var s = v.GetRawText();
        return s.Length > 200 ? s[..200] + "…" : s;
    }

    // "publishedAt" → "Published at", "source_url" → "Source url".
    private static string Humanize(string key)
    {
        var spaced = new StringBuilder();
        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (c is '_' or '-') { spaced.Append(' '); continue; }
            if (char.IsUpper(c) && i > 0 && !char.IsUpper(key[i - 1])) spaced.Append(' ');
            spaced.Append(c);
        }
        var s = spaced.ToString().Trim();
        return s.Length == 0 ? key : char.ToUpperInvariant(s[0]) + s[1..];
    }

    private static string EscAttr(string s) => Esc(s).Replace("\"", "&quot;");

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
        ".cards{display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:12px;margin:8px 0}" +
        ".card{border:1px solid #e5e7eb;border-radius:10px;padding:12px 14px;background:#fff}" +
        ".ctitle{font-size:14px;font-weight:600;margin-bottom:6px;line-height:1.3}.ctitle a{color:#1d4ed8;text-decoration:none}" +
        "dl.kv{display:grid;grid-template-columns:auto 1fr;gap:2px 10px;margin:4px 0;font-size:12.5px}" +
        "dl.kv dt{color:#6b7280}dl.kv dd{margin:0;word-break:break-word}a{color:#1d4ed8}ul{font-size:13px}" +
        "</style></head><body>";

    private static byte[] Bytes(StringBuilder sb) => Encoding.UTF8.GetBytes(sb.ToString());

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
