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

    private static (Job job, JobRun run) Sample(params PostJobActionKind[] actions)
    {
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "nightly-etl", null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0, postJobActions: actions);
        var run = JobRun.Start(job.Id, job.ProjectId, T0, WorkloadSnapshot.From(mapSpec, null, 1));
        run.Complete(new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "{\"rows\":[{\"label\":\"a\",\"n\":3},{\"label\":\"b\",\"n\":7}]}", "ok"),
        }, null, T0.AddSeconds(2));
        return (job, run);
    }

    [Fact]
    public async Task Chart_uses_the_llm_output_when_it_returns_usable_html()
    {
        var (job, run) = Sample(PostJobActionKind.Chart);
        var store = new FakeStore();
        var llm = new FakeLlm("```html\n<!doctype html><html><body><svg><rect/></svg></body></html>\n```");
        var svc = new PostJobActionService(store, new FakeLinks(), new FakeUow(), new FakeClock(), llm);

        await svc.RunAsync(job, run);

        var chart = Encoding.UTF8.GetString(Assert.Single(store.Objects, o => o.Key.EndsWith("chart.html")).Content);
        Assert.Contains("<svg>", chart);          // the LLM's chart, fences stripped
        Assert.DoesNotContain("shard outcomes", chart); // not the deterministic fallback
        Assert.Contains("chart", llm.LastSystem!, StringComparison.OrdinalIgnoreCase); // chart-specific instruction used
    }

    [Fact]
    public async Task Chart_falls_back_to_the_deterministic_svg_when_the_llm_is_off()
    {
        var (job, run) = Sample(PostJobActionKind.Chart);
        var store = new FakeStore();
        var svc = new PostJobActionService(store, new FakeLinks(), new FakeUow(), new FakeClock(), llm: null);

        await svc.RunAsync(job, run);

        var chart = Encoding.UTF8.GetString(Assert.Single(store.Objects).Content);
        Assert.Contains("shard outcomes", chart);  // the deterministic outcome chart
        Assert.Contains("<svg", chart);
    }

    [Fact]
    public async Task Chart_is_themed_for_the_portal_whether_llm_or_fallback()
    {
        // LLM chart: our theme is injected even though the model set its own light background.
        var (job, run) = Sample(PostJobActionKind.Chart);
        var store = new FakeStore();
        var llm = new FakeLlm("<!doctype html><html><head></head><body style=\"background:#fff\"><svg><rect/></svg></body></html>");
        await new PostJobActionService(store, new FakeLinks(), new FakeUow(), new FakeClock(), llm).RunAsync(job, run);
        var llmChart = Encoding.UTF8.GetString(Assert.Single(store.Objects).Content);
        Assert.Contains("pc-chart-theme", llmChart);
        Assert.Contains("--pc-bg", llmChart);

        // Deterministic fallback chart is themed the same way.
        var (job2, run2) = Sample(PostJobActionKind.Chart);
        var store2 = new FakeStore();
        await new PostJobActionService(store2, new FakeLinks(), new FakeUow(), new FakeClock(), llm: null).RunAsync(job2, run2);
        var fallbackChart = Encoding.UTF8.GetString(Assert.Single(store2.Objects).Content);
        Assert.Contains("pc-chart-theme", fallbackChart);
        Assert.Contains("shard outcomes", fallbackChart); // still the deterministic chart underneath
    }

    [Fact]
    public async Task Chart_falls_back_when_the_llm_returns_non_html()
    {
        var (job, run) = Sample(PostJobActionKind.Chart);
        var store = new FakeStore();
        var llm = new FakeLlm("I cannot draw a chart for this data.");
        var svc = new PostJobActionService(store, new FakeLinks(), new FakeUow(), new FakeClock(), llm);

        await svc.RunAsync(job, run);

        var chart = Encoding.UTF8.GetString(Assert.Single(store.Objects).Content);
        Assert.Contains("shard outcomes", chart);  // rejected the prose, used the fallback
    }

    [Fact]
    public async Task Html_returned_by_the_job_is_stored_as_an_artifact_without_any_action_configured()
    {
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "html-job", null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0); // NO post-job actions
        var run = JobRun.Start(job.Id, job.ProjectId, T0, WorkloadSnapshot.From(mapSpec, null, 1));
        run.Complete(new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "<!doctype html><html><body><h1>hi</h1></body></html>", "ok"),
        }, null, T0.AddSeconds(2));

        var store = new FakeStore();
        var links = new FakeLinks();
        await new PostJobActionService(store, links, new FakeUow(), new FakeClock()).RunAsync(job, run);

        var obj = Assert.Single(store.Objects, o => o.Key.EndsWith("shard-0.html"));
        Assert.Contains("<h1>hi</h1>", Encoding.UTF8.GetString(obj.Content));
        Assert.Single(links.Links, l => l.Kind == PostJobActionKind.HtmlOutput);
    }

    [Fact]
    public async Task Default_outputs_are_produced_automatically_when_no_actions_are_configured()
    {
        var (job, run) = Sample(); // no configured actions
        var store = new FakeStore();
        var links = new FakeLinks();

        await new PostJobActionService(store, links, new FakeUow(), new FakeClock()).RunAsync(job, run);

        // Every run yields the report/chart/CSV trio without per-job setup.
        Assert.Single(store.Objects, o => o.Key.EndsWith("report.html"));
        Assert.Single(store.Objects, o => o.Key.EndsWith("chart.html"));
        Assert.Single(store.Objects, o => o.Key.EndsWith("run.csv"));
        Assert.Equal(3, links.Links.Count);
    }

    [Fact]
    public async Task Configured_actions_replace_the_default_outputs()
    {
        var (job, run) = Sample(PostJobActionKind.Csv);
        var store = new FakeStore();

        await new PostJobActionService(store, new FakeLinks(), new FakeUow(), new FakeClock()).RunAsync(job, run);

        Assert.Single(store.Objects, o => o.Key.EndsWith("run.csv"));
        Assert.DoesNotContain(store.Objects, o => o.Key.EndsWith("report.html"));
        Assert.DoesNotContain(store.Objects, o => o.Key.EndsWith("chart.html"));
    }

    [Fact]
    public async Task Json_artifacts_are_not_captured_as_html_even_when_they_contain_markup()
    {
        var (job, run) = Sample(); // JSON shard artifact, no actions
        var store = new FakeStore();
        await new PostJobActionService(store, new FakeLinks(), new FakeUow(), new FakeClock()).RunAsync(job, run);
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
        await new PostJobActionService(store2, new FakeLinks(), new FakeUow(), new FakeClock()).RunAsync(job2, run2);
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
        await new PostJobActionService(store, new FakeLinks(), new FakeUow(), new FakeClock()).RunAsync(job, run);

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
        await new PostJobActionService(store, links, new FakeUow(), new FakeClock()).RunAsync(job, run);

        var obj = Assert.Single(store.Objects, o => o.Key.EndsWith("out/0/listings.pdf"));
        Assert.Equal(pdfBytes, obj.Content);
        Assert.DoesNotContain(store.Objects, o => o.Key.EndsWith("notes.txt")); // plain text is not auto-stored
        var link = Assert.Single(links.Links, l => l.Kind == PostJobActionKind.RawBundle);
        Assert.Equal("application/pdf", link.ContentType);
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
        await new PostJobActionService(store, links, new FakeUow(), new FakeClock()).RunAsync(job, run);

        var obj = Assert.Single(store.Objects, o => o.Key.EndsWith("out/0/chart.png"));
        Assert.Equal(pngBytes, obj.Content);
        Assert.Single(links.Links, l => l.ContentType == "image/png");
    }

    // ── fakes ───────────────────────────────────────────────────────────────────────────────────────
    private sealed class FakeLlm(string response) : ILlmGateway
    {
        public bool IsEnabled => true;
        public string? LastSystem { get; private set; }
        public Task<string> GenerateAsync(string system, string user, CancellationToken ct = default)
        {
            LastSystem = system;
            return Task.FromResult(response);
        }
    }

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
