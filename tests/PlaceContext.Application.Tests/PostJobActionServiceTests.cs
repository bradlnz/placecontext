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
        public Task AddAsync(RunArtifactLink link, CancellationToken ct = default) => Task.CompletedTask;
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
