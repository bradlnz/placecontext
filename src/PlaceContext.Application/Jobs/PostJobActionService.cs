using System.Text;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.Logging;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Runs a job's configured post-job actions after a run completes: builds each output (HTML report,
/// chart, CSV, raw files) from the run's artifacts, stores it in the object store, and records a
/// <see cref="RunArtifactLink"/> so the portal/TUI can surface it. Entirely best-effort — every action
/// is isolated so one failure never fails the run or blocks the others.
/// </summary>
public sealed class PostJobActionService
{
    private readonly IObjectStore _store;
    private readonly IRunArtifactLinkRepository _links;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILlmGateway? _llm;
    private readonly ILogger<PostJobActionService>? _log;

    public PostJobActionService(IObjectStore store, IRunArtifactLinkRepository links, IUnitOfWork uow, IClock clock,
        ILlmGateway? llm = null, ILogger<PostJobActionService>? log = null)
    {
        _store = store;
        _links = links;
        _uow = uow;
        _clock = clock;
        _llm = llm;
        _log = log;
    }

    public async Task RunAsync(Job job, JobRun run, CancellationToken ct = default)
    {
        if (job.PostJobActions.Count == 0) return;
        if (!_store.IsEnabled)
        {
            _log?.LogWarning("Post-job actions configured for job {JobId} but the object store is disabled — skipping.", job.Id);
            return;
        }

        var bucket = _store.ReportsBucket;
        var added = false;

        foreach (var action in job.PostJobActions)
        {
            try
            {
                switch (action)
                {
                    case PostJobActionKind.HtmlReport:
                        added |= await StoreAsync(job, run, action, await BuildHtmlReportAsync(job, run, ct), bucket, ct);
                        break;
                    case PostJobActionKind.Chart:
                        added |= await StoreAsync(job, run, action, await BuildChartAsync(job, run, ct), bucket, ct);
                        break;
                    case PostJobActionKind.Csv:
                        added |= await StoreAsync(job, run, action, PostJobArtifacts.Csv(run), bucket, ct);
                        break;
                    case PostJobActionKind.RawBundle:
                        foreach (var f in PostJobArtifacts.RawBundle(run))
                            added |= await StoreAsync(job, run, action, f, bucket, ct);
                        break;
                }
            }
            catch (Exception ex)
            {
                // Best-effort: a failing action (e.g. object store hiccup) must not fail the run, but it
                // must be visible — a silently-empty reports bucket is the worst failure mode.
                _log?.LogWarning(ex, "Post-job action {Action} failed for run {RunId} (job {JobId}).",
                    action, run.Id, job.Id);
            }
        }

        if (added) await _uow.SaveChangesAsync(ct);
    }

    private const string HtmlSystemPrompt =
        "You are given the JSON output of a data job. Produce a SINGLE, complete, self-contained HTML5 " +
        "document that presents this information clearly for a person — a title plus the records as a " +
        "table or cards, with any URLs as clickable links. Use only minimal inline CSS: no external " +
        "stylesheets, scripts, images, fonts, or CDNs. Be faithful to the data; do not invent values. " +
        "Output ONLY the HTML — no markdown fences, no commentary before or after.";

    private const string ChartSystemPrompt =
        "You are given the JSON output of a data job. Produce a SINGLE, complete, self-contained HTML5 " +
        "document whose body is ONE chart that best visualizes the quantitative shape of this data — a " +
        "bar, line, or pie chart drawn as inline <svg> (use numeric fields, or counts of categorical " +
        "values, whichever the data supports). Give it a title, label the axes or include a legend, and " +
        "print the actual values as text on the chart. Use ONLY inline <svg> and minimal inline CSS: no " +
        "external stylesheets, scripts, images, fonts, or CDNs. Be faithful to the data; do not invent " +
        "values. If the data carries no chartable quantities, render a small summary table instead. " +
        "Output ONLY the HTML — no markdown fences, no commentary before or after.";

    // The HTML report: let the local LLM (Gemma via Ollama) render the data into HTML; fall back to the
    // deterministic data renderer when the LLM is disabled, errors, or returns something that isn't HTML.
    private Task<PostJobArtifacts.BuiltFile> BuildHtmlReportAsync(Job job, JobRun run, CancellationToken ct) =>
        BuildLlmHtmlAsync(HtmlSystemPrompt, run, "report.html", "HTML report",
            () => PostJobArtifacts.HtmlReport(job, run), ct);

    // The chart: let the LLM draw the data as an inline-SVG chart; fall back to the deterministic shard-
    // outcome chart when the LLM is disabled, errors, or returns something that isn't HTML.
    private Task<PostJobArtifacts.BuiltFile> BuildChartAsync(Job job, JobRun run, CancellationToken ct) =>
        BuildLlmHtmlAsync(ChartSystemPrompt, run, "chart.html", "Chart",
            () => PostJobArtifacts.Chart(job, run), ct);

    // Shared LLM→HTML path: feed the run's primary data to the gateway under the given instruction, accept
    // the response only if it extracts to usable HTML, and otherwise return the deterministic fallback.
    private async Task<PostJobArtifacts.BuiltFile> BuildLlmHtmlAsync(string system, JobRun run,
        string fileName, string title, Func<PostJobArtifacts.BuiltFile> fallback, CancellationToken ct)
    {
        var data = PrimaryData(run);
        if (_llm is { IsEnabled: true } && !string.IsNullOrWhiteSpace(data))
        {
            try
            {
                var raw = await _llm.GenerateAsync(system, Truncate(data, 12000), ct);
                var html = ExtractHtml(raw);
                if (LooksLikeHtml(html))
                    return new PostJobArtifacts.BuiltFile(fileName, Encoding.UTF8.GetBytes(html),
                        "text/html; charset=utf-8", title);
                _log?.LogWarning("LLM {Title} for run {RunId} wasn't usable HTML (raw starts: {Raw}) — using the deterministic renderer.",
                    title, run.Id, Truncate(raw.Trim(), 200));
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "LLM {Title} failed for run {RunId} — using the deterministic renderer.", title, run.Id);
            }
        }
        return fallback(); // reliable fallback
    }

    // The job's primary data: the reduce artifact when present (final aggregate), else the shard artifacts.
    private static string PrimaryData(JobRun run)
    {
        if (run.ReduceResult?.Artifact is { Length: > 0 } r) return r;
        var parts = run.ShardResults.Where(s => !string.IsNullOrWhiteSpace(s.Artifact)).Select(s => s.Artifact!);
        return string.Join("\n", parts);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    // Pull the HTML out of an LLM response: take the content of a ``` fenced block if one appears
    // anywhere, then narrow to the document root (dropping any prose before <!doctype/<html and after
    // </html>). Robust to the model wrapping the doc in fences and/or adding commentary around it.
    private static string ExtractHtml(string raw)
    {
        var s = raw.Trim();

        var fence = s.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var afterTicks = s.IndexOf('\n', fence);     // skip the ```/```html line
            if (afterTicks >= 0)
            {
                var rest = s[(afterTicks + 1)..];
                var close = rest.IndexOf("```", StringComparison.Ordinal);
                s = (close >= 0 ? rest[..close] : rest).Trim();
            }
        }

        var lower = s.ToLowerInvariant();
        var start = lower.IndexOf("<!doctype", StringComparison.Ordinal);
        if (start < 0) start = lower.IndexOf("<html", StringComparison.Ordinal);
        if (start > 0) s = s[start..];

        var end = s.ToLowerInvariant().LastIndexOf("</html>", StringComparison.Ordinal);
        if (end >= 0) s = s[..(end + "</html>".Length)];

        return s.Trim();
    }

    private static bool LooksLikeHtml(string s) =>
        !string.IsNullOrWhiteSpace(s) && s.Contains('<') &&
        (s.Contains("<html", StringComparison.OrdinalIgnoreCase) || s.Contains("<!doctype", StringComparison.OrdinalIgnoreCase)
         || s.Contains("<body", StringComparison.OrdinalIgnoreCase) || s.Contains("<table", StringComparison.OrdinalIgnoreCase)
         || s.Contains("<div", StringComparison.OrdinalIgnoreCase) || s.Contains("<h1", StringComparison.OrdinalIgnoreCase)
         || s.Contains("<ul", StringComparison.OrdinalIgnoreCase) || s.Contains("<svg", StringComparison.OrdinalIgnoreCase));

    private async Task<bool> StoreAsync(Job job, JobRun run, PostJobActionKind kind,
        PostJobArtifacts.BuiltFile file, string bucket, CancellationToken ct)
    {
        var key = $"runs/{run.Id:N}/{file.FileName}";
        await _store.PutAsync(bucket, key, file.Content, file.ContentType, ct);
        await _links.AddAsync(RunArtifactLink.Create(
            run.Id, job.Id, run.ProjectId, kind, file.Title, bucket, key,
            file.ContentType, file.Content.LongLength, _clock.UtcNow), ct);
        return true;
    }
}
