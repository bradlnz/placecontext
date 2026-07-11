using System.Text;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.Logging;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Generates a run's stored artifacts after it completes. The job's declared
/// <see cref="Job.ReturnType"/> drives the run's primary artifact — generated for EVERY run, with
/// a raw-result fallback if the typed build fails, so a completed run always yields at least one
/// openable artifact. Explicitly configured post-job actions are additive extras on top.
/// Failures are isolated so artifact generation never fails the run itself.
/// </summary>
public sealed class PostJobActionService
{
    private readonly IObjectStore _store;
    private readonly IRunArtifactLinkRepository _links;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<PostJobActionService>? _log;

    public PostJobActionService(IObjectStore store, IRunArtifactLinkRepository links, IUnitOfWork uow, IClock clock,
        ILogger<PostJobActionService>? log = null)
    {
        _store = store;
        _links = links;
        _uow = uow;
        _clock = clock;
        _log = log;
    }

    public async Task RunAsync(Job job, JobRun run, CancellationToken ct = default)
    {
        if (!_store.IsEnabled)
        {
            // Artifacts are mandatory (driven by the job's return type) — a disabled object store
            // means none can be produced, which must be loudly visible, not a silent skip.
            _log?.LogError("Object store disabled — cannot generate the {ReturnType} artifact for run {RunId} (job {JobId}).",
                job.ReturnType, run.Id, job.Id);
            return;
        }

        var bucket = _store.ReportsBucket;
        var added = false;

        // HTML the job itself returned is always captured — no configuration needed.
        try
        {
            added |= await StoreJobHtmlOutputsAsync(job, run, bucket, ct);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Capturing job HTML outputs failed for run {RunId} (job {JobId}).", run.Id, job.Id);
        }

        // The declared return type drives the run's primary artifact — generated for every run.
        added |= await StorePrimaryArtifactAsync(job, run, bucket, ct);

        // Explicitly configured actions are additive extras on top of the typed primary artifact.
        foreach (var action in job.PostJobActions)
        {
            try
            {
                switch (action)
                {
                    case PostJobActionKind.HtmlReport:
                        added |= await StoreAsync(job, run, action, PostJobArtifacts.HtmlReport(job, run), bucket, ct);
                        break;
                    case PostJobActionKind.Chart:
                        added |= await StoreAsync(job, run, action, StyledChart(job, run), bucket, ct);
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

    // ── The mandatory, return-type-driven primary artifact ──────────────────────────────────────────

    /// <summary>
    /// Builds and stores the run's primary artifact as declared by the job's return type. Never
    /// silently produces nothing: if the typed build fails, the raw result is stored instead, so a
    /// completed run always has an artifact (short of the object store itself being down).
    /// </summary>
    private async Task<bool> StorePrimaryArtifactAsync(Job job, JobRun run, string bucket, CancellationToken ct)
    {
        try
        {
            var (kind, file) = BuildPrimaryArtifact(job, run);
            return await StoreAsync(job, run, kind, file, bucket, ct);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Typed {ReturnType} artifact failed for run {RunId} (job {JobId}) — storing the raw result instead.",
                job.ReturnType, run.Id, job.Id);
            try
            {
                return await StoreAsync(job, run, PostJobActionKind.RawBundle, JsonResult(run), bucket, ct);
            }
            catch (Exception fallbackEx)
            {
                _log?.LogError(fallbackEx, "Mandatory artifact could not be stored for run {RunId} (job {JobId}).", run.Id, job.Id);
                return false;
            }
        }
    }

    private (PostJobActionKind Kind, PostJobArtifacts.BuiltFile File) BuildPrimaryArtifact(
        Job job, JobRun run) => job.ReturnType switch
    {
        JobReturnType.Table => (PostJobActionKind.HtmlReport, PostJobArtifacts.HtmlReport(job, run)),
        JobReturnType.Chart => (PostJobActionKind.Chart, StyledChart(job, run)),
        JobReturnType.Csv => (PostJobActionKind.Csv, PostJobArtifacts.Csv(run)),
        JobReturnType.Html => (PostJobActionKind.HtmlOutput, HtmlResult(job, run)),
        JobReturnType.Text => (PostJobActionKind.RawBundle, TextResult(run)),
        JobReturnType.Pdf => (PostJobActionKind.RawBundle, FileResult(job, run, PdfExtensions)),
        JobReturnType.Image => (PostJobActionKind.RawBundle, FileResult(job, run, ImageExtensions)),
        JobReturnType.Video => (PostJobActionKind.RawBundle, FileResult(job, run, VideoExtensions)),
        _ => (PostJobActionKind.RawBundle, JsonResult(run)), // Json
    };

    private static readonly string[] PdfExtensions = { ".pdf" };
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg" };
    private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov", ".avi", ".mkv" };

    // File return types (Pdf/Image/Video): the job emits its result as a file to /out, and the
    // first matching file (reduce step wins over shards) is the run's primary artifact. When the
    // job emitted no matching file, the deterministic report stands in so the mandatory artifact
    // still exists (and the mismatch is logged).
    private PostJobArtifacts.BuiltFile FileResult(Job job, JobRun run, string[] extensions)
    {
        var files = (run.ReduceResult?.Artifacts ?? Array.Empty<RunArtifact>())
            .Concat(run.ShardResults.OrderBy(s => s.Index).SelectMany(s => s.Artifacts));
        foreach (var f in files)
        {
            var ext = Path.GetExtension(f.Name).ToLowerInvariant();
            if (!extensions.Contains(ext)) continue;
            var contentType = DocContentType(f.Name) ?? "application/octet-stream";
            return new PostJobArtifacts.BuiltFile(Path.GetFileName(f.Name), f.GetBytes(), contentType,
                Path.GetFileName(f.Name));
        }
        _log?.LogWarning("Run {RunId} (job {JobId}) declares a {ReturnType} return type but emitted no matching file to /out — rendering a report instead.",
            run.Id, job.Id, job.ReturnType);
        return PostJobArtifacts.HtmlReport(job, run);
    }

    // The chart page is themed (LlmHtml.StyleChart) so it reads correctly embedded in the portal's
    // run-history panel.
    private static PostJobArtifacts.BuiltFile StyledChart(Job job, JobRun run)
    {
        var file = PostJobArtifacts.Chart(job, run);
        return file with { Content = Encoding.UTF8.GetBytes(LlmHtml.StyleChart(Encoding.UTF8.GetString(file.Content))) };
    }

    // Even an empty return yields a well-formed artifact — mandatory generation has no
    // "nothing to store" path.
    private static PostJobArtifacts.BuiltFile JsonResult(JobRun run)
    {
        var data = PrimaryData(run);
        if (string.IsNullOrWhiteSpace(data)) data = "null";
        return new PostJobArtifacts.BuiltFile("result.json", Encoding.UTF8.GetBytes(data),
            "application/json", "JSON result");
    }

    private static PostJobArtifacts.BuiltFile TextResult(JobRun run) =>
        new("result.txt", Encoding.UTF8.GetBytes(PrimaryData(run)),
            "text/plain; charset=utf-8", "Text result");

    // A job declared Html must return an HTML document; when it doesn't, the deterministic report
    // stands in so the mandatory artifact still exists (and the mismatch is logged).
    private PostJobArtifacts.BuiltFile HtmlResult(Job job, JobRun run)
    {
        var data = PrimaryData(run);
        if (IsHtmlDocument(data))
            return new PostJobArtifacts.BuiltFile("output.html", Encoding.UTF8.GetBytes(data),
                "text/html; charset=utf-8", "HTML output");
        _log?.LogWarning("Run {RunId} (job {JobId}) declares an Html return type but did not return an HTML document — rendering a report instead.",
            run.Id, job.Id);
        return PostJobArtifacts.HtmlReport(job, run);
    }

    // Documents returned by the job itself — a shard/reduce stdout artifact that is an HTML document,
    // or an emitted .html/.pdf/image output file — become stored, openable artifacts without any action
    // configured: documents are meant to be opened, not read as escaped strings in the run detail.
    // Emitted files go under out/ so they can never collide with action outputs (report.html, chart.html).
    private async Task<bool> StoreJobHtmlOutputsAsync(Job job, JobRun run, string bucket, CancellationToken ct)
    {
        var added = false;

        async Task StoreDocAsync(string fileName, byte[] content, string title, PostJobActionKind kind, string contentType) =>
            added |= await StoreAsync(job, run, kind,
                new PostJobArtifacts.BuiltFile(fileName, content, contentType, title), bucket, ct);

        Task StoreHtmlAsync(string fileName, string content, string title) =>
            StoreDocAsync(fileName, Encoding.UTF8.GetBytes(content), title, PostJobActionKind.HtmlOutput, "text/html; charset=utf-8");

        async Task StoreFilesAsync(IEnumerable<RunArtifact> files, string prefix)
        {
            foreach (var f in files)
            {
                if (IsHtmlFile(f) && !f.IsBinary) await StoreHtmlAsync($"{prefix}/{f.Name}", f.Content, f.Name);
                else if (DocContentType(f.Name) is { } contentType)
                    await StoreDocAsync($"{prefix}/{f.Name}", f.GetBytes(), f.Name, PostJobActionKind.RawBundle, contentType);
            }
        }

        foreach (var shard in run.ShardResults)
        {
            if (shard.Artifact is { } a && IsHtmlDocument(a))
                await StoreHtmlAsync($"shard-{shard.Index}.html", a, $"Shard {shard.Index} HTML output");
            await StoreFilesAsync(shard.Artifacts, $"out/{shard.Index}");
        }

        if (run.ReduceResult is { } reduce)
        {
            if (reduce.Artifact is { } r && IsHtmlDocument(r))
                await StoreHtmlAsync("reduce.html", r, "Reduce HTML output");
            await StoreFilesAsync(reduce.Artifacts, "out/reduce");
        }

        return added;
    }

    private static bool IsHtmlFile(RunArtifact f) =>
        f.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
        f.Name.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

    // Emitted files that are documents to open (not data to read inline) — auto-stored with the
    // content type the browser needs to render them. Returns null for everything else.
    private static string? DocContentType(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            _ => null,
        };
    }

    // The whole artifact must BE an HTML document — a JSON artifact that merely contains markup in a
    // string value stays JSON.
    private static bool IsHtmlDocument(string s) =>
        s.TrimStart().StartsWith("<", StringComparison.Ordinal) && LlmHtml.LooksLikeHtml(s);

    // The job's primary data: the reduce artifact when present (final aggregate), else the shard artifacts.
    private static string PrimaryData(JobRun run)
    {
        if (run.ReduceResult?.Artifact is { Length: > 0 } r) return r;
        var parts = run.ShardResults.Where(s => !string.IsNullOrWhiteSpace(s.Artifact)).Select(s => s.Artifact!);
        return string.Join("\n", parts);
    }

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
