using System.Text;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Application.Tests;

public class PostJobActionServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

    private static (Job job, JobRun run) Sample(
        JobReturnType returnType = JobReturnType.Json, params PostJobActionKind[] actions)
    {
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "nightly-etl", null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0,
            postJobActions: actions, returnType: returnType);
        var run = JobRun.Start(job.Id, job.ProjectId, T0, WorkloadSnapshot.From(mapSpec, null, 1));
        run.Complete(new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "{\"rows\":[{\"label\":\"a\",\"n\":3},{\"label\":\"b\",\"n\":7}]}", "ok"),
        }, null, T0.AddSeconds(2));
        return (job, run);
    }

    private static PostJobActionService Service(FakeStore store, FakeLinks? links = null) =>
        new(store, links ?? new FakeLinks(), new FakeUow(), new FakeClock());

    // ── The return type drives the mandatory primary artifact ────────────────────────────────────────

    [Fact]
    public async Task Json_return_type_stores_the_primary_result_verbatim()
    {
        var (job, run) = Sample(JobReturnType.Json);
        var store = new FakeStore();
        var links = new FakeLinks();

        await Service(store, links).RunAsync(job, run);

        var obj = Assert.Single(store.Objects, o => o.Key.EndsWith("result.json"));
        Assert.Contains("\"rows\"", Encoding.UTF8.GetString(obj.Content));
        Assert.Single(links.Links); // exactly the mandatory primary artifact, nothing else
    }

    [Fact]
    public async Task Table_return_type_renders_the_html_report()
    {
        var (job, run) = Sample(JobReturnType.Table);
        var store = new FakeStore();

        await Service(store).RunAsync(job, run);

        var report = Encoding.UTF8.GetString(Assert.Single(store.Objects, o => o.Key.EndsWith("report.html")).Content);
        Assert.Contains("nightly-etl", report);
    }

    [Fact]
    public async Task Chart_return_type_renders_the_deterministic_themed_chart()
    {
        var (job, run) = Sample(JobReturnType.Chart);
        var store = new FakeStore();

        await Service(store).RunAsync(job, run);

        var chart = Encoding.UTF8.GetString(Assert.Single(store.Objects, o => o.Key.EndsWith("chart.html")).Content);
        Assert.Contains("shard outcomes", chart);
        Assert.Contains("<svg", chart);
        Assert.Contains("pc-chart-theme", chart); // themed for the portal's run-history panel
        Assert.Contains("--pc-bg", chart);
    }

    [Fact]
    public async Task Csv_return_type_flattens_the_run_to_csv()
    {
        var (job, run) = Sample(JobReturnType.Csv);
        var store = new FakeStore();

        await Service(store).RunAsync(job, run);

        Assert.Single(store.Objects, o => o.Key.EndsWith("run.csv"));
    }

    [Fact]
    public async Task Text_return_type_stores_the_primary_result_as_text()
    {
        var (job, run) = Sample(JobReturnType.Text);
        var store = new FakeStore();

        await Service(store).RunAsync(job, run);

        Assert.Single(store.Objects, o => o.Key.EndsWith("result.txt"));
    }

    [Fact]
    public async Task Html_return_type_stores_the_returned_document_openable_as_is()
    {
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "html-job", null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0, returnType: JobReturnType.Html);
        var run = JobRun.Start(job.Id, job.ProjectId, T0, WorkloadSnapshot.From(mapSpec, null, 1));
        run.Complete(new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "<!doctype html><html><body><h1>hi</h1></body></html>", "ok"),
        }, null, T0.AddSeconds(2));

        var store = new FakeStore();
        await Service(store).RunAsync(job, run);

        var obj = Assert.Single(store.Objects, o => o.Key.EndsWith("output.html"));
        Assert.Contains("<h1>hi</h1>", Encoding.UTF8.GetString(obj.Content));
    }

    [Fact]
    public async Task Html_return_type_falls_back_to_the_report_when_the_job_returned_json()
    {
        var (job, run) = Sample(JobReturnType.Html); // shard artifact is JSON, not an HTML document
        var store = new FakeStore();

        await Service(store).RunAsync(job, run);

        Assert.DoesNotContain(store.Objects, o => o.Key.EndsWith("output.html"));
        Assert.Single(store.Objects, o => o.Key.EndsWith("report.html")); // mandatory artifact still exists
    }

    [Fact]
    public async Task Pdf_return_type_stores_the_emitted_pdf_as_the_primary_artifact()
    {
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x00, 0xE2 };
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "pdf-return", null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0, returnType: JobReturnType.Pdf);
        var run = JobRun.Start(job.Id, job.ProjectId, T0, WorkloadSnapshot.From(mapSpec, null, 1));
        run.Complete(new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "{}", "ok",
                new[] { new RunArtifact("report.pdf", Convert.ToBase64String(pdfBytes), isBinary: true) }),
        }, null, T0.AddSeconds(2));

        var store = new FakeStore();
        var links = new FakeLinks();
        await Service(store, links).RunAsync(job, run);

        // Stored twice by design: auto-captured under out/ and as the typed primary artifact.
        var primary = Assert.Single(store.Objects, o => o.Key.EndsWith($"runs/{run.Id:N}/report.pdf"));
        Assert.Equal(pdfBytes, primary.Content);
        Assert.Contains(links.Links, l => l.ContentType == "application/pdf");
    }

    [Fact]
    public async Task Image_return_type_falls_back_to_the_report_when_no_image_was_emitted()
    {
        var (job, run) = Sample(JobReturnType.Image); // JSON artifact, no emitted files
        var store = new FakeStore();

        await Service(store).RunAsync(job, run);

        Assert.Single(store.Objects, o => o.Key.EndsWith("report.html")); // mandatory artifact still exists
    }

    [Fact]
    public async Task Every_run_yields_an_artifact_even_when_it_produced_no_data()
    {
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "silent-job", null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0);
        var run = JobRun.Start(job.Id, job.ProjectId, T0, WorkloadSnapshot.From(mapSpec, null, 1));
        run.Complete(new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, null, "ok"), // no artifact at all
        }, null, T0.AddSeconds(2));

        var store = new FakeStore();
        var links = new FakeLinks();
        await Service(store, links).RunAsync(job, run);

        var obj = Assert.Single(store.Objects, o => o.Key.EndsWith("result.json"));
        Assert.Equal("null", Encoding.UTF8.GetString(obj.Content)); // well-formed even when empty
        Assert.Single(links.Links);
    }

    [Fact]
    public async Task Configured_actions_are_additive_extras_on_top_of_the_primary_artifact()
    {
        var (job, run) = Sample(JobReturnType.Json, PostJobActionKind.Csv);
        var store = new FakeStore();

        await Service(store).RunAsync(job, run);

        Assert.Single(store.Objects, o => o.Key.EndsWith("result.json")); // the return-type artifact
        Assert.Single(store.Objects, o => o.Key.EndsWith("run.csv"));     // plus the configured extra
        Assert.Equal(2, store.Objects.Count);
    }

    // ── Auto-captured documents the job emitted (unchanged behaviour) ─────────────────────────────────

    [Fact]
    public async Task Html_returned_by_the_job_is_stored_as_an_artifact_without_any_action_configured()
    {
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "html-job", null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0); // Json return type, no actions
        var run = JobRun.Start(job.Id, job.ProjectId, T0, WorkloadSnapshot.From(mapSpec, null, 1));
        run.Complete(new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "<!doctype html><html><body><h1>hi</h1></body></html>", "ok"),
        }, null, T0.AddSeconds(2));

        var store = new FakeStore();
        var links = new FakeLinks();
        await Service(store, links).RunAsync(job, run);

        var obj = Assert.Single(store.Objects, o => o.Key.EndsWith("shard-0.html"));
        Assert.Contains("<h1>hi</h1>", Encoding.UTF8.GetString(obj.Content));
        Assert.Single(links.Links, l => l.Kind == PostJobActionKind.HtmlOutput);
    }

    [Fact]
    public async Task Json_artifacts_are_not_captured_as_html_even_when_they_contain_markup()
    {
        var (job, run) = Sample(); // JSON shard artifact, no actions
        var store = new FakeStore();
        await Service(store).RunAsync(job, run);
        Assert.DoesNotContain(store.Objects, o => o.Key.Contains("shard-")); // not captured as a document

        // JSON that carries an HTML snippet inside a string value stays JSON.
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        var job2 = Job.Create(Guid.NewGuid(), "j", null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0);
        var run2 = JobRun.Start(job2.Id, job2.ProjectId, T0, WorkloadSnapshot.From(mapSpec, null, 1));
        run2.Complete(new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "{\"html\":\"<div>x</div>\"}", "ok"),
        }, null, T0.AddSeconds(2));
        var store2 = new FakeStore();
        await Service(store2).RunAsync(job2, run2);
        Assert.DoesNotContain(store2.Objects, o => o.Key.Contains("shard-"));
    }

    [Fact]
    public async Task Emitted_html_files_are_stored_per_shard_so_names_never_collide()
    {
        var mapSpec = new MapSpec("img", new[] { "{}", "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "html-files", null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0);
        var run = JobRun.Start(job.Id, job.ProjectId, T0, WorkloadSnapshot.From(mapSpec, null, 1));
        run.Complete(new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "{}", "ok", new[] { new RunArtifact("page.html", "<h1>a</h1>") }),
            new ShardResult(1, 0, WorkloadOutcome.Succeeded, "{}", "ok", new[] { new RunArtifact("page.html", "<h1>b</h1>") }),
        }, null, T0.AddSeconds(2));

        var store = new FakeStore();
        await Service(store).RunAsync(job, run);

        Assert.Single(store.Objects, o => o.Key.EndsWith("out/0/page.html"));
        Assert.Single(store.Objects, o => o.Key.EndsWith("out/1/page.html"));
    }

    [Fact]
    public async Task Emitted_pdfs_are_stored_as_artifacts_like_html()
    {
        // A binary PDF (invalid UTF-8 bytes — what the old text pipeline corrupted) rides as base64
        // in the RunArtifact and must land in the object store byte-identical.
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x00 };
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "pdf-job", null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0); // NO post-job actions
        var run = JobRun.Start(job.Id, job.ProjectId, T0, WorkloadSnapshot.From(mapSpec, null, 1));
        run.Complete(new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "{}", "ok",
                new[]
                {
                    new RunArtifact("listings.pdf", Convert.ToBase64String(pdfBytes), isBinary: true),
                    new RunArtifact("notes.txt", "skip me"),
                }),
        }, null, T0.AddSeconds(2));

        var store = new FakeStore();
        var links = new FakeLinks();
        await Service(store, links).RunAsync(job, run);

        var obj = Assert.Single(store.Objects, o => o.Key.EndsWith("out/0/listings.pdf"));
        Assert.Equal(pdfBytes, obj.Content);
        Assert.DoesNotContain(store.Objects, o => o.Key.EndsWith("notes.txt")); // plain text is not auto-stored
        var link = Assert.Single(links.Links, l => l.ContentType == "application/pdf");
        Assert.Equal(PostJobActionKind.RawBundle, link.Kind);
    }

    [Fact]
    public async Task Emitted_images_are_stored_with_their_image_content_type()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "img-job", null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0);
        var run = JobRun.Start(job.Id, job.ProjectId, T0, WorkloadSnapshot.From(mapSpec, null, 1));
        run.Complete(new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "{}", "ok",
                new[] { new RunArtifact("chart.png", Convert.ToBase64String(pngBytes), isBinary: true) }),
        }, null, T0.AddSeconds(2));

        var store = new FakeStore();
        var links = new FakeLinks();
        await Service(store, links).RunAsync(job, run);

        var obj = Assert.Single(store.Objects, o => o.Key.EndsWith("out/0/chart.png"));
        Assert.Equal(pngBytes, obj.Content);
        Assert.Single(links.Links, l => l.ContentType == "image/png");
    }

    // ── fakes ───────────────────────────────────────────────────────────────────────────────────────
    private sealed class FakeStore : IObjectStore
    {
        public List<(string Key, byte[] Content)> Objects { get; } = new();
        public bool IsEnabled => true;
        public string ReportsBucket => "placecontext-reports";
        public Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken ct = default)
        {
            Objects.Add((key, content));
            return Task.CompletedTask;
        }
        public Task<ObjectDownload?> OpenReadAsync(string bucket, string key, CancellationToken ct = default)
            => Task.FromResult<ObjectDownload?>(null);
        public Task DeleteAsync(string bucket, string key, CancellationToken ct = default)
        {
            Objects.RemoveAll(o => o.Key == key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLinks : IRunArtifactLinkRepository
    {
        public List<RunArtifactLink> Links { get; } = new();
        public Task AddAsync(RunArtifactLink link, CancellationToken ct = default)
        {
            Links.Add(link);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<RunArtifactLink>> ListForRunAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunArtifactLink>>(Array.Empty<RunArtifactLink>());
        public Task<RunArtifactLink?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<RunArtifactLink?>(null);
        public Task<IReadOnlyList<RunArtifactLink>> ListForJobAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunArtifactLink>>(Array.Empty<RunArtifactLink>());
        public Task<IReadOnlyList<RunArtifactLink>> ListRecentAsync(int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunArtifactLink>>(Links.Take(take).ToList());
        public Task<IReadOnlyList<RunArtifactLink>> ListForProjectAsync(Guid projectId, int take, string? search = null, CancellationToken ct = default)
        {
            IEnumerable<RunArtifactLink> q = Links.Where(l => l.ProjectId == projectId);
            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(l => l.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                              || l.Kind.ToString().Contains(search, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult<IReadOnlyList<RunArtifactLink>>(q.Take(take).ToList());
        }
        public Task RemoveAsync(Guid id, CancellationToken ct = default)
        {
            Links.RemoveAll(l => l.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUow : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => T0;
    }
}
