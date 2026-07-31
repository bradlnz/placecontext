using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;

namespace PlaceContext.Application.Tests;

public sealed class JobTestCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Json_subset_allows_extra_object_properties()
    {
        var test = Test(JobTestAssertionType.JsonSubset, """{"customer":{"status":"active"}}""");
        var run = Run("""{"customer":{"status":"active","name":"Ada"},"traceId":"123"}""");

        var result = RunJobTestCaseHandler.Evaluate(
            test, run, RunJobChainHandler.PrimaryOutput(run));

        Assert.Equal("Passed", result.Status);
    }

    [Fact]
    public void Json_subset_reports_structural_mismatch()
    {
        var test = Test(JobTestAssertionType.JsonSubset, """{"status":"active"}""");
        var run = Run("""{"status":"inactive"}""");

        var result = RunJobTestCaseHandler.Evaluate(
            test, run, RunJobChainHandler.PrimaryOutput(run));

        Assert.Equal("Failed", result.Status);
        Assert.Contains("expected JSON structure", result.Message);
    }

    [Fact]
    public void Output_assertion_cannot_pass_when_job_failed()
    {
        var test = Test(JobTestAssertionType.OutputContains, "error");
        var run = Run("error", "Failed");

        var result = RunJobTestCaseHandler.Evaluate(test, run, "error");

        Assert.Equal("Failed", result.Status);
        Assert.Contains("finished as Failed", result.Message);
    }

    [Theory]
    [InlineData(JobTestAssertionType.OutputEquals, "hello", " hello ", "Passed")]
    [InlineData(JobTestAssertionType.OutputEquals, "hello", "hello!", "Failed")]
    [InlineData(JobTestAssertionType.OutputContains, "ell", "hello", "Passed")]
    public void Text_assertions_are_predictable(
        JobTestAssertionType assertion, string expected, string actual, string status)
    {
        var result = RunJobTestCaseHandler.Evaluate(Test(assertion, expected), Run(actual), actual);
        Assert.Equal(status, result.Status);
    }

    [Fact]
    public async Task Test_owned_code_receives_job_result_and_controls_pass_status()
    {
        var projectId = Guid.NewGuid();
        var job = Job.Create(projectId, "customer lookup", null,
            new MapSpec("image", new[] { "{}" }, new Dictionary<string, string>()),
            null, 1, ExitCodePolicy.Default, Now);
        var jobs = new InMemoryJobRepository();
        await jobs.AddAsync(job);
        var test = new JobTestCaseRecord(
            Guid.NewGuid(), projectId, job.Id, "active customer", """{"id":"123"}""",
            JobTestAssertionType.Succeeds, null, true, "NotRun", null, null,
            null, null, null, Now, Now, "python", "test.py",
            new[] { new CodeFileDto("test.py", "print('ok')") }, false);
        var store = new MemoryTestStore(test);
        var workloads = new FakeWorkloadRunner();
        workloads.EnqueueResult(new WorkloadRunResult(0, null, "2 assertions passed", ""));
        var runner = new StubJobRunner(Run("""{"status":"active"}"""));
        var handler = new RunJobTestCaseHandler(
            store, jobs, runner, workloads, new FakeClock(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(new RunJobTestCaseCommand(test.Id));

        Assert.Equal("Passed", result.LastStatus);
        Assert.Contains("2 assertions passed", result.LastMessage);
        var request = Assert.Single(workloads.ReceivedRequests);
        Assert.Equal("python", request.RuntimeId);
        Assert.Equal("test.py", request.Entrypoint);
        using var stdin = System.Text.Json.JsonDocument.Parse(request.StdinPayload);
        Assert.Equal("active", stdin.RootElement.GetProperty("run")
            .GetProperty("output").GetProperty("status").GetString());
        Assert.Equal("123", stdin.RootElement.GetProperty("input").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Nonzero_test_code_exit_fails_the_test()
    {
        var projectId = Guid.NewGuid();
        var job = Job.Create(projectId, "lookup", null,
            new MapSpec("image", new[] { "{}" }, new Dictionary<string, string>()),
            null, 1, ExitCodePolicy.Default, Now);
        var jobs = new InMemoryJobRepository();
        await jobs.AddAsync(job);
        var test = new JobTestCaseRecord(
            Guid.NewGuid(), projectId, job.Id, "invalid result", null,
            JobTestAssertionType.Succeeds, null, true, "NotRun", null, null,
            null, null, null, Now, Now, "node", "test.js",
            new[] { new CodeFileDto("test.js", "process.exit(1)") }, false);
        var store = new MemoryTestStore(test);
        var workloads = new FakeWorkloadRunner();
        workloads.EnqueueResult(new WorkloadRunResult(7, null, "", "expected total 3"));
        var handler = new RunJobTestCaseHandler(
            store, jobs, new StubJobRunner(Run("{}")), workloads, new FakeClock(Now));

        var result = await handler.HandleAsync(new RunJobTestCaseCommand(test.Id));

        Assert.Equal("Failed", result.LastStatus);
        Assert.Contains("exited 7", result.LastMessage);
        Assert.Contains("expected total 3", result.LastMessage);
    }

    private static JobTestCaseRecord Test(JobTestAssertionType assertion, string? expected) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", null, assertion, expected,
        true, "NotRun", null, null, null, null, null, Now, Now,
        null, null, Array.Empty<CodeFileDto>(), false);

    private static JobRunDetailView Run(string output, string status = "Succeeded") => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), status, Now, Now.AddSeconds(1),
        new[]
        {
            new ShardResultView(0, status == "Succeeded" ? 0 : 1, status, output, null,
            Array.Empty<RunArtifactView>()),
        },
        null,
        new JobRunSnapshotView("code", "test.py", null, null, 1, 1, false));

    private sealed class StubJobRunner : IJobRunner
    {
        private readonly JobRunDetailView _result;
        public StubJobRunner(JobRunDetailView result) => _result = result;
        public Task<JobRunDetailView> RunAsync(
            Guid jobId, string? inputPayload = null, Guid? runId = null,
            Guid? replayOfRunId = null, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class MemoryTestStore : IJobTestStore
    {
        private JobTestCaseRecord _test;
        public MemoryTestStore(JobTestCaseRecord test) => _test = test;
        public Task<JobTestCaseRecord?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<JobTestCaseRecord?>(_test.Id == id ? _test : null);
        public Task<IReadOnlyList<JobTestCaseRecord>> ListForProjectAsync(
            Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<JobTestCaseRecord>>(
                _test.ProjectId == projectId ? new[] { _test } : Array.Empty<JobTestCaseRecord>());
        public Task SaveAsync(JobTestCaseRecord test, CancellationToken ct = default)
        {
            _test = test;
            return Task.CompletedTask;
        }
        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(false);
    }
}
