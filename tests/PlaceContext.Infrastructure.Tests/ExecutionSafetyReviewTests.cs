using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Build;
using PlaceContext.Application.Features;
using PlaceContext.Application.Workload;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Workload;
using PlaceContext.Domain.Entities;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ExecutionSafetyReviewTests
{
    [Theory]
    [InlineData("[]", false)]
    [InlineData("[{\"agentActivity\":null}]", false)]
    [InlineData("[{\"agentActivity\":[{\"kind\":\"retry\"}]}]", false)]
    [InlineData("[{\"agentActivity\":[{\"kind\":\"execution_blocked\"}]}]", true)]
    public void Queue_reconciliation_recognizes_persisted_safety_rejections(string steps, bool blocked)
        => Assert.Equal(blocked, PlaceContext.Infrastructure.Scheduling.RpcChainSubmissionBackgroundService.SafetyBlocked(steps));

    private static WorkloadRunRequest Request() => new(null, "{\"tag\":true}",
        new Dictionary<string, string> { ["API_KEY"] = "vault-secret-not-for-review" },
        [("reduce evidence", "/in/result.json")], "test-run",
        [("index.js", "console.log(JSON.parse(require('fs').readFileSync(0, 'utf8')))")], "node", "index.js",
        AllowNetworkEgress: false, TimeoutSeconds: 30, ProjectId: Guid.NewGuid());

    [Fact]
    public async Task Approved_job_reviews_complete_evidence_without_vault_values_before_execution()
    {
        var reviewer = new SafetyReviewer(.95);
        var workload = new Workload(() => Assert.Equal(1, reviewer.Calls));
        var runner = new ReviewedWorkloadRunner(workload, reviewer);
        var request = Request();
        await runner.RunAsync(request);
        Assert.Same(request, workload.Request);
        Assert.Equal(request.ProjectId, reviewer.Project);
        var evidence = JsonNode.Parse(reviewer.State!)!;
        Assert.Equal(request.CodeFiles![0].Content, evidence["files"]![0]!["Content"]!.GetValue<string>());
        Assert.Equal(request.StdinPayload, evidence["input"]!.GetValue<string>());
        Assert.Contains("reduce evidence", reviewer.State);
        Assert.Contains("API_KEY", reviewer.State);
        Assert.DoesNotContain("vault-secret-not-for-review", reviewer.State);
        await runner.CancelAsync("test-run");
        Assert.Equal("test-run", workload.Cancelled);
    }

    [Fact]
    public async Task Rejected_job_saves_failed_run_and_review_reason_without_launching_workload()
    {
        var now = DateTimeOffset.UtcNow;
        var job = Job.Create(Guid.NewGuid(), "reviewed job", null, new MapSpec("worker", ["{}"], new Dictionary<string, string>()),
            null, 1, ExitCodePolicy.Default, now);
        var jobs = new InMemoryJobRepository(); await jobs.AddAsync(job);
        var runs = new InMemoryJobRunRepository(); var workload = new Workload(); var reviewer = new SafetyReviewer(.1);
        var handler = new RunJobHandler(jobs, runs, new ReviewedWorkloadRunner(workload, reviewer), new RecordingUnitOfWork(), new FakeClock(now));
        var runId = Guid.NewGuid();
        await Assert.ThrowsAsync<ExecutionSafetyException>(() => handler.HandleAsync(new RunJobCommand(job.Id, RunId: runId)));
        var saved = await runs.GetByIdAsync(runId);
        Assert.Equal(JobRunStatus.Failed, saved!.Status);
        Assert.Equal(job.ProjectId, reviewer.Project);
        Assert.Null(workload.Request);
        Assert.Contains("AI safety review", Assert.Single(saved.ShardResults).Log);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.89)]
    [InlineData(-1)]
    [InlineData(1.1)]
    public async Task Dangerous_uncertain_or_invalid_decision_never_executes_or_retries(double confidence)
    {
        var reviewer = new SafetyReviewer(confidence);
        var workload = new Workload();
        var runner = new ReviewedWorkloadRunner(workload, reviewer);
        await Assert.ThrowsAsync<ExecutionSafetyException>(() => WorkflowStepExecution.RunAsync(
            new StepExecutionPolicy(3, 0), token => runner.RunAsync(Request(), token), default));
        Assert.Null(workload.Request);
        Assert.Equal(1, reviewer.Calls);
    }

    [Fact]
    public async Task Malformed_missing_unavailable_or_oversized_review_blocks_execution()
    {
        foreach (var response in new[] { new JsonObject(), new JsonObject { ["noul"] = "true" }, new JsonObject { ["noul"] = true } })
            await Assert.ThrowsAsync<ExecutionSafetyException>(() => ExecutionSafetyReview.RequireAsync(new SafetyReviewer(1) { Response = response }, new { code = "test" }, null, default));
        await Assert.ThrowsAsync<ExecutionSafetyException>(() => ExecutionSafetyReview.RequireAsync(null, new { }, null, default));
        var unavailable = new SafetyReviewer(1) { Error = new HttpRequestException("Provider unavailable") };
        var workload = new Workload();
        await Assert.ThrowsAsync<ExecutionSafetyException>(() => new ReviewedWorkloadRunner(workload, unavailable).RunAsync(Request()));
        Assert.Null(workload.Request);
        var reviewer = new SafetyReviewer(1);
        await Assert.ThrowsAsync<ExecutionSafetyException>(() => new ReviewedWorkloadRunner(workload, reviewer).RunAsync(Request() with { StdinPayload = new string('x', 65_536) }));
        Assert.Equal(0, reviewer.Calls);
        Assert.Null(workload.Request);
    }

    [Fact]
    public async Task Cancellation_after_review_cannot_start_workload()
    {
        using var cancellation = new CancellationTokenSource();
        var reviewer = new SafetyReviewer(1) { OnReview = cancellation.Cancel };
        var workload = new Workload();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ReviewedWorkloadRunner(workload, reviewer).RunAsync(Request(), cancellation.Token));
        Assert.Null(workload.Request);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Container_build_and_resolved_image_are_reviewed_before_docker(bool build)
    {
        var reviewer = new SafetyReviewer(.1);
        using var runner = new ContainerWorkflowBackgroundService(Options.Create(new ContainerWorkflowOptions { Enabled = true }),
            Options.Create(new WorkloadRunnerOptions()), NullLogger<ContainerWorkflowBackgroundService>.Instance, reviewer);
        var project = Guid.NewGuid();
        ChainAction action = build ? new ContainerBuildChainAction("FROM alpine\nCOPY attack.sh /attack.sh\nRUN /attack.sh")
            : new ContainerRunChainAction(imageField: "chosen", command: ["sh", "-c", "rm -rf /data"]);
        await Assert.ThrowsAsync<ExecutionSafetyException>(() => runner.ExecuteAsync(action, project, Guid.NewGuid(), 0,
            """{"chosen":"alpine:3","files":{"attack.sh":"echo suspicious"}}""", default));
        Assert.Equal(project, reviewer.Project);
        Assert.Contains(build ? "echo suspicious" : "alpine:3", reviewer.State);
        Assert.Contains(build ? "Dockerfile" : "rm -rf /data", reviewer.State);
        Assert.Equal(1, reviewer.Calls);
    }

    private sealed class Workload(Action? beforeRun = null) : IWorkloadRunner
    {
        public WorkloadRunRequest? Request;
        public string? Cancelled;
        public Task<WorkloadRunResult> RunAsync(WorkloadRunRequest request, CancellationToken ct = default)
        {
            beforeRun?.Invoke();
            Request = request;
            return Task.FromResult(new WorkloadRunResult(0, null, "ok", ""));
        }
        public Task CancelAsync(string correlationId, CancellationToken ct = default) { Cancelled = correlationId; return Task.CompletedTask; }
    }
}

internal sealed class SafetyReviewer(double confidence = 1) : IOutcomeReviewer
{
    public int Calls;
    public string? State;
    public Guid? Project;
    public JsonObject? Response;
    public Exception? Error;
    public Action? OnReview;
    public Task<JsonObject> EvaluateAsync(string question, string? state, CancellationToken ct, Guid? projectId = null)
    {
        Calls++;
        State = state;
        Project = projectId;
        Assert.Contains("untrusted data", question);
        OnReview?.Invoke();
        if (Error is not null) throw Error;
        return Task.FromResult(Response ?? new JsonObject { ["noul"] = confidence });
    }
    public Task<JsonObject> ScoreAsync(IReadOnlyList<ScoreCriterion> criteria, string state, CancellationToken ct, Guid? projectId = null) => throw new NotSupportedException();
}
