using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

public class PersistentWorkloadRunnerTests
{
    private const string Image = "registry.example/ossen-native@sha256:abc";
    private static WorkloadRunRequest Request(string image = Image) => new(
        image, "{\"site\":{}}", new Dictionary<string, string> { ["OSSEN_JOB"] = "site-screen" },
        [], "run-123", null, null, null, AllowNetworkEgress: false);

    [Fact]
    public async Task Inline_rust_sends_source_to_permanent_workers_without_an_image()
    {
        var handler = new Handler();
        var fallback = new Fallback();
        var request = Request() with { Image = null, RuntimeId = "rust", Entrypoint = "rust_jobs/src/main.rs", CodeFiles = [("rust_jobs/src/main.rs", "fn main() {}") ] };
        var runner = Create(handler, fallback);
        await runner.RunAsync(request);
        var payload = JsonDocument.Parse(Assert.Single(handler.Payloads));
        Assert.Equal("fn main() {}", payload.RootElement.GetProperty("codeFiles")[0].GetProperty("content").GetString());
        Assert.Equal(0, fallback.Runs);
        handler.Status = HttpStatusCode.Forbidden;
        await Assert.ThrowsAsync<HttpRequestException>(() => runner.RunAsync(request));
        Assert.Equal(0, fallback.Runs);
    }

    [Fact]
    public async Task Native_jobs_use_persistent_workers_with_artifacts_and_never_fall_back()
    {
        var handler = new Handler();
        var fallback = new Fallback();
        var runner = Create(handler, fallback);
        var result = await runner.RunAsync(Request());
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("JVBERg==", Assert.Single(result.Artifacts!).Content);
        Assert.Equal(0, fallback.Runs);
        var payload = JsonDocument.Parse(Assert.Single(handler.Payloads));
        Assert.Equal("site-screen", payload.RootElement.GetProperty("stage").GetString());
        Assert.Equal("run-123", payload.RootElement.GetProperty("correlationId").GetString());
        Assert.False(payload.RootElement.GetProperty("allowNetworkEgress").GetBoolean());
        Assert.Equal("Bearer", handler.Authorization);

        handler.Status = HttpStatusCode.UnprocessableEntity;
        await Assert.ThrowsAsync<HttpRequestException>(() => runner.RunAsync(Request()));
        Assert.Equal(0, fallback.Runs);
    }

    [Fact]
    public async Task Busy_workers_are_retried_but_unregistered_images_keep_the_existing_runner()
    {
        var handler = new Handler { BusyOnce = true };
        var fallback = new Fallback();
        var runner = Create(handler, fallback);
        await runner.RunAsync(Request());
        Assert.Equal(2, handler.Payloads.Count);
        await runner.RunAsync(Request("other-image"));
        Assert.Equal(1, fallback.Runs);
        Assert.Equal(2, handler.Payloads.Count);
    }

    private static PersistentWorkloadRunner Create(Handler handler, Fallback fallback) => new(
        Options.Create(new WorkloadRunnerOptions
        {
            PersistentRunnerImages = [Image], PersistentRunnerUrls = ["http://runner-0:8080", "http://runner-1:8080"],
            PersistentRunnerToken = "test-runner-token-longer-than-32-characters"
        }), new Factory(handler), fallback);

    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }
    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Payloads { get; } = [];
        public string? Authorization { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool BusyOnce { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Authorization = request.Headers.Authorization?.Scheme;
            Payloads.Add(await request.Content!.ReadAsStringAsync(ct));
            if (BusyOnce) { BusyOnce = false; return new(HttpStatusCode.TooManyRequests); }
            return new(Status) { Content = JsonContent.Create(new WorkloadRunResult(0, "{}", "{}", "",
                [new WorkloadArtifact("report.pdf", "JVBERg==", true)])) };
        }
    }
    private sealed class Fallback : IWorkloadRunner
    {
        public int Runs { get; private set; }
        public Task<WorkloadRunResult> RunAsync(WorkloadRunRequest request, CancellationToken ct = default)
        { Runs++; return Task.FromResult(new WorkloadRunResult(0, "{}", "{}", "")); }
        public Task CancelAsync(string correlationId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
